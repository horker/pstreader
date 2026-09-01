// Copyright (c) 2016, Dijji, and released under Ms-PL.  This can be found in the root of this distribution. 

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace XstReader
{
    /// <summary>
    /// This class implements the NDB (Node Database) layer, Which provides lower-level storage facilities
    /// </summary>
    class NDB
    {
        private const UInt16 FormatVersionAnsiOutlook97To2002 = 0x0e;
        private const UInt16 FormatVersionAnsiOutlook2002 = 0x0f;
        private const UInt16 FormatVersionUnicode = 0x17;
        private const UInt16 FormatVersionUnicode4K = 0x24;

        private string fullName;
        private EbCryptMethod bCryptMethod;
        private BTree<Node> nodeTree = new BTree<Node>();
        private BTree<DataRef> dataTree = new BTree<DataRef>();

        // deferReadingNodes is a switch set only here
        // If true, reading node and data tree b-trees is deferred until look-up time,
        // and only required portions of the tree are read
        // If false, the full b-trees are read at file open time
        // The former setting (the default) gets the folder structure almost instantly after opening,
        // and consumes less memory in many scenarios,
        // but may make opening a folder slower and net less efficient
        private bool deferReadingNodes = true;
        private Action<TreeIntermediate> deferredReadAction;

        public int BlockSize { get; private set; } = 8192;
        public int BlockSize4K { get; private set; } = 65536;
        public bool IsUnicode { get; private set; }
        public bool IsUnicode4K { get; private set; } = false;

        #region Public methods

        public NDB(string fullName)
        {
            this.fullName = fullName;
            deferredReadAction = new Action<TreeIntermediate>(p => this.ReadDeferredIndex(p));
        }

        // Prepare to read the contents of a file
        public void Initialise()
        {
            ReadHeaderAndIndexes();
        }
        
        // Return a file stream that will allow the caller to read the current file
        public FileStream GetReadStream()
        {
            return new FileStream(fullName, FileMode.Open, FileAccess.Read);
        }

        // Decrypt the contents of a data buffer if necessary
        public void Decrypt(ref byte[] buffer, UInt32 key, int offset = 0, int length = 0)
        {
            Crypto.Decrypt(ref buffer, bCryptMethod, key, offset, length > 0 ? length : buffer.Length - offset);
        }

        // Look up a node in the main node b-tree
        public Node LookupNode(NID nid)
        {
            return nodeTree.Lookup(nid.dwValue, deferredReadAction);
        }

        // Look up a data block in the main data block b-tree
        public DataRef LookupDataBlock(UInt64 dataBid)
        {
            // Clear the LSB, which is reserved, but sometimes set
            return dataTree.Lookup(dataBid & 0xfffffffe, deferredReadAction);
        }

        // Read a sub-node's b-tree from the specified data block
        public BTree<Node> ReadSubNodeBtree(FileStream fs, UInt64 subDataBid)
        {
            var tree = new BTree<Node>();
            if (IsUnicode)
                ReadSubNodeBtreeUnicode(fs, subDataBid, tree.Root);
            else
                ReadSubNodeBtreeANSI(fs, subDataBid, tree.Root);
            return tree;
        }

        // Look up a node in a sub-node's b-tree
        public static Node LookupSubNode(BTree<Node> subNodeTree, NID nid)
        {
            if (subNodeTree == null)
                throw new XstException("No sub node data tree found");

            return subNodeTree.Lookup(nid.dwValue);
        }

        // Read raw data, accessed via a sub node
        // If it has a multiblock structure, return all of the blocks' contents concatenated
        public byte[] ReadSubNodeDataBlock(FileStream fs, BTree<Node> subNodeTree, NID nid)
        {
            if (!nid.HasValue)
                return null;
            var n = LookupSubNode(subNodeTree, nid);
            if (n == null)
                throw new XstException("Node not found in sub node tree");
            if (n.SubDataBid != 0)
                throw new XstException("Sub-nodes of sub-nodes not yet implemented");
            return ReadDataBlock(fs, n.DataBid);
        }

        // Read all the data blocks for a given BID, concatenating them into a single buffer
        public byte[] ReadDataBlock(FileStream fs, UInt64 dataBid)
        {
            int offset = 0;
            return ReadDataBlockInternal(fs, dataBid, ref offset);
        }

        // Read all the data blocks for a given BID, enumerating them one by one
        public IEnumerable<byte[]> ReadDataBlocks(FileStream fs, UInt64 dataBid)
        {
            foreach (var buf in ReadDataBlocksInternal(fs, dataBid))
            {
                yield return buf;
            }
        }

        // Copy data to the specified file stream
        // We write each external data block as we read it, so that we never have more than one in memory at the same time
        public void CopyDataBlocks(FileStream fs, Stream s, UInt64 dataBid)
        {
            foreach (var buf in ReadDataBlocksInternal(fs, dataBid))
            {
                s.Write(buf, 0, buf.Length);
            }
        }

        // Some nodes use sub-nodes to hold local data, so we need to give access to both levels
        public Node LookupNodeAndReadItsSubNodeBtree(FileStream fs, NID nid, out BTree<Node> subNodeTree)
        {
            subNodeTree = null;
            var rn = LookupNode(nid);
            if (rn == null)
                throw new XstException("Node block does not exist");
            // If there is a sub-node, read its btree so that we can resolve references to nodes in it later
            if (rn.SubDataBid != 0)
            {
                subNodeTree = ReadSubNodeBtree(fs, rn.SubDataBid);
            }
            return rn;
        }

        // Some sub-nodes use sub-sub-nodes to hold local data, so we need to give access to both levels
        public Node LookupSubNodeAndReadItsSubNodeBtree(FileStream fs, BTree<Node> subNodeTree, NID nid, out BTree<Node> childSubNodeTree)
        {
            childSubNodeTree = null;
            var rn = LookupSubNode(subNodeTree, nid);
            if (rn == null)
                throw new XstException("Node block does not exist");
            // If there is a sub-node, read its btree so that we can resolve references to nodes in it later
            if (rn.SubDataBid != 0)
            {
                childSubNodeTree = ReadSubNodeBtree(fs, rn.SubDataBid);
            }
            return rn;
        }

        #endregion


        #region Private methods

        // Read the file header, and the B trees that give us access to nodes and data blocks
        private void ReadHeaderAndIndexes()
        {
            using (var fs = GetReadStream())
            {
                var header1Length = Marshal.SizeOf(typeof(FileHeader1));
                var header1Bytes = new byte[header1Length];
                fs.ReadExactly(header1Bytes, 0, header1Bytes.Length);
                var h = Map.MapType<FileHeader1>(header1Bytes);

                if (h.dwMagic != 0x4e444221)
                    throw new XstException("File is not a .ost or .pst file: the magic cookie is missing");
                if (h.wMagicClient != 0x4d53)
                    throw new XstException("File is not a .ost or .pst file: the client magic is invalid");
                if (h.bPlatformCreate != 0x01 || h.bPlatformAccess != 0x01)
                    throw new XstException("OST/PST header platform markers are invalid");

                if (IsUnicode4KFormat(h.wVer))
                {
                    // This value indicates the use of 4K pages, as opposed to 512 bytes
                    // It is used only in .ost files, and was introduced in Office 2013
                    // It is not documented in [MS-PST], being .ost only
                    var h2Bytes = new byte[Marshal.SizeOf(typeof(FileHeader2Unicode))];
                    fs.ReadExactly(h2Bytes, 0, h2Bytes.Length);
                    var h2 = Map.MapType<FileHeader2Unicode>(h2Bytes);
                    bCryptMethod = h2.bCryptMethod;
                    IsUnicode = true;
                    IsUnicode4K = true;
                    if (bCryptMethod == EbCryptMethod.NDB_CRYPT_EDPCRYPTED)
                        throw new XstException("This OST/PST file is protected with Windows Information Protection (WIP). Open it on the managed Windows device and account that owns it, or decrypt/export it with Outlook or enterprise admin tooling before using XstReader.");
                    ReadBTPageUnicode4K(fs, h2.root.BREFNBT.ib, nodeTree.Root);
                    ReadBTPageUnicode4K(fs, h2.root.BREFBBT.ib, dataTree.Root);
                }
                else if (IsUnicodeFormat(h.wVer))
                {
                    var h2Bytes = new byte[Marshal.SizeOf(typeof(FileHeader2Unicode))];
                    fs.ReadExactly(h2Bytes, 0, h2Bytes.Length);
                    ValidateUnicodeHeader(header1Bytes, h2Bytes);
                    var h2 = Map.MapType<FileHeader2Unicode>(h2Bytes);
                    bCryptMethod = h2.bCryptMethod;
                    IsUnicode = true;
                    if (bCryptMethod == EbCryptMethod.NDB_CRYPT_EDPCRYPTED)
                        throw new XstException("This OST/PST file is protected with Windows Information Protection (WIP). Open it on the managed Windows device and account that owns it, or decrypt/export it with Outlook or enterprise admin tooling before using XstReader.");
                    ReadBTPageUnicode(fs, h2.root.BREFNBT.ib, nodeTree.Root);
                    ReadBTPageUnicode(fs, h2.root.BREFBBT.ib, dataTree.Root);
                }
                else if (IsAnsiFormat(h.wVer))
                {
                    var h2Bytes = new byte[Marshal.SizeOf(typeof(FileHeader2ANSI))];
                    fs.ReadExactly(h2Bytes, 0, h2Bytes.Length);
                    ValidateAnsiHeader(header1Bytes, h2Bytes);
                    var h2 = Map.MapType<FileHeader2ANSI>(h2Bytes);
                    bCryptMethod = h2.bCryptMethod;
                    IsUnicode = false;
                    if (bCryptMethod == EbCryptMethod.NDB_CRYPT_EDPCRYPTED)
                        throw new XstException("This OST/PST file is protected with Windows Information Protection (WIP). Open it on the managed Windows device and account that owns it, or decrypt/export it with Outlook or enterprise admin tooling before using XstReader.");
                    ReadBTPageANSI(fs, h2.root.BREFNBT.ib, nodeTree.Root);
                    ReadBTPageANSI(fs, h2.root.BREFBBT.ib, dataTree.Root);
                }
                else
                    throw new XstException($"Unrecognised OST/PST header version 0x{h.wVer:x4}");
            }
        }

        private static bool IsUnicode4KFormat(UInt16 version)
        {
            return version == FormatVersionUnicode4K;
        }

        private static bool IsUnicodeFormat(UInt16 version)
        {
            return version >= FormatVersionUnicode;
        }

        private static bool IsAnsiFormat(UInt16 version)
        {
            return version == FormatVersionAnsiOutlook97To2002 || version == FormatVersionAnsiOutlook2002;
        }

        // A callback to be used when searching a tree to read part of the index whose loading has been deferred
        private void ReadDeferredIndex(TreeIntermediate inter)
        {
            using (var fs = GetReadStream())
            {
                if (IsUnicode4K)
                    ReadBTPageUnicode4K(fs, (ulong)inter.fileOffset, inter);
                else if (IsUnicode)
                    ReadBTPageUnicode(fs, (ulong)inter.fileOffset, inter);
                else
                    ReadBTPageANSI(fs, (ulong)inter.fileOffset, inter);

                // Don't read it again
                inter.fileOffset = null;
            }
        }
        // Read a page containing part of a node or data block B-tree, and build the corresponding data structure
        private void ReadBTPageUnicode(FileStream fs, ulong fileOffset, TreeIntermediate parent)
        {
            fs.Seek((long)fileOffset, SeekOrigin.Begin);
            var pageBytes = new byte[512];
            fs.ReadExactly(pageBytes, 0, pageBytes.Length);
            ValidatePage(pageBytes, fileOffset, isUnicode: true);
            var p = Map.MapType<BTPAGEUnicode>(pageBytes);

            // read entries
            for (int i = 0; i < p.cEnt; i++)
            {
                if (p.cLevel > 0)
                {
                    BTENTRYUnicode e;
                    unsafe
                    {
                        e = Map.MapType<BTENTRYUnicode>(p.rgentries, LayoutsU.BTPAGEEntryBytes, i * p.cbEnt, p.cbEnt);
                    }
                    var inter = new TreeIntermediate { Key = e.btkey };
                    parent.Children.Add(inter);

                    if (deferReadingNodes)
                        inter.fileOffset = e.BREF.ib;
                    else
                    {
                        // Read child nodes in tree
                        ReadBTPageUnicode(fs, e.BREF.ib, inter);
                        inter.fileOffset = null;
                    }
                }
                else
                {
                    if (p.pageTrailer.ptype == Eptype.ptypeNBT)
                    {
                        unsafe
                        {
                            var e = Map.MapType<NBTENTRYUnicode>(p.rgentries, LayoutsU.BTPAGEEntryBytes, i * p.cbEnt, p.cbEnt);
                            var nb = new Node { Key = e.nid.dwValue, Type = e.nid.nidType, DataBid = e.bidData, SubDataBid = e.bidSub, Parent = e.nidParent };
                            parent.Children.Add(nb);
                        }
                    }
                    else if (p.pageTrailer.ptype == Eptype.ptypeBBT)
                    {
                        unsafe
                        {
                            var e = Map.MapType<BBTENTRYUnicode>(p.rgentries, LayoutsU.BTPAGEEntryBytes, i * p.cbEnt, p.cbEnt);
                            parent.Children.Add(new DataRef { Key = e.BREF.bid, Offset = e.BREF.ib, Length = e.cb });
                        }
                    }
                    else
                        throw new XstException("Unexpected page entry type");
                }
            }
        }

        // Read a page containing part of a node or data block B-tree, and build the corresponding data structure
        private void ReadBTPageUnicode4K(FileStream fs, ulong fileOffset, TreeIntermediate parent)
        {
            fs.Seek((long)fileOffset, SeekOrigin.Begin);
            var pageBytes = new byte[4096];
            fs.ReadExactly(pageBytes, 0, pageBytes.Length);
            var p = Map.MapType<BTPAGEUnicode4K>(pageBytes);

            // read entries
            for (int i = 0; i < p.cEnt; i++)
            {
                if (p.cLevel > 0)
                {
                    BTENTRYUnicode e;
                    unsafe
                    {
                        e = Map.MapType<BTENTRYUnicode>(p.rgentries, LayoutsU4K.BTPAGEEntryBytes, i * p.cbEnt, p.cbEnt);
                    }
                    var inter = new TreeIntermediate { Key = e.btkey };
                    parent.Children.Add(inter);

                    if (deferReadingNodes)
                        inter.fileOffset = e.BREF.ib;
                    else
                    {
                        // Read child nodes in tree
                        ReadBTPageUnicode4K(fs, e.BREF.ib, inter);
                        inter.fileOffset = null;
                    }
                }
                else
                {
                    if (p.pageTrailer.ptype == Eptype.ptypeNBT)
                    {
                        unsafe
                        {
                            var e = Map.MapType<NBTENTRYUnicode>(p.rgentries, LayoutsU4K.BTPAGEEntryBytes, i * p.cbEnt, p.cbEnt);
                            var nb = new Node { Key = e.nid.dwValue, Type = e.nid.nidType, DataBid = e.bidData, SubDataBid = e.bidSub, Parent = e.nidParent };
                            parent.Children.Add(nb);
                        }
                    }
                    else if (p.pageTrailer.ptype == Eptype.ptypeBBT)
                    {
                        unsafe
                        {
                            var e = Map.MapType<BBTENTRYUnicode4K>(p.rgentries, LayoutsU4K.BTPAGEEntryBytes, i * p.cbEnt, p.cbEnt);
                            parent.Children.Add(new DataRef { Key = e.BREF.bid, Offset = e.BREF.ib, Length = e.cbStored, InflatedLength = e.cbInflated });
                        }
                    }
                    else
                        throw new XstException("Unexpected page entry type");
                }
            }
        }

        private void ReadBTPageANSI(FileStream fs, ulong fileOffset, TreeIntermediate parent)
        {
            fs.Seek((long)fileOffset, SeekOrigin.Begin);
            var pageBytes = new byte[512];
            fs.ReadExactly(pageBytes, 0, pageBytes.Length);
            ValidatePage(pageBytes, fileOffset, isUnicode: false);
            var p = Map.MapType<BTPAGEANSI>(pageBytes);

            // read entries
            for (int i = 0; i < p.cEnt; i++)
            {
                if (p.cLevel > 0)
                {
                    BTENTRYANSI e;
                    unsafe
                    {
                        e = Map.MapType<BTENTRYANSI>(p.rgentries, LayoutsA.BTPAGEEntryBytes, i * p.cbEnt, p.cbEnt);
                    }
                    var inter = new TreeIntermediate { Key = e.btkey };
                    parent.Children.Add(inter);

                    if (deferReadingNodes)
                        inter.fileOffset = e.BREF.ib;
                    else
                    {
                        // Read child nodes in tree
                        ReadBTPageANSI(fs, e.BREF.ib, inter);
                        inter.fileOffset = null;
                    }
                }
                else
                {
                    if (p.pageTrailer.ptype == Eptype.ptypeNBT)
                    {
                        unsafe
                        {
                            var e = Map.MapType<NBTENTRYANSI>(p.rgentries, LayoutsA.BTPAGEEntryBytes, i * p.cbEnt, p.cbEnt);
                            var nb = new Node { Key = e.nid.dwValue, Type = e.nid.nidType, DataBid = e.bidData, SubDataBid = e.bidSub, Parent = e.nidParent };
                            parent.Children.Add(nb);
                        }
                    }
                    else if (p.pageTrailer.ptype == Eptype.ptypeBBT)
                    {
                        unsafe
                        {
                            var e = Map.MapType<BBTENTRYANSI>(p.rgentries, LayoutsA.BTPAGEEntryBytes, i * p.cbEnt, p.cbEnt);
                            parent.Children.Add(new DataRef { Key = e.BREF.bid, Offset = e.BREF.ib, Length = e.cb });
                        }
                    }
                    else
                        throw new XstException("Unexpected page entry type");
                }
            }
        }

        // Return all the data blocks referenced by a particular data Id
        private IEnumerable<byte[]> ReadDataBlocksInternal(FileStream fs, UInt64 dataBid, uint totalLength = 0)
        {
            var rb = LookupDataBlock(dataBid);
            int read;
            byte[] buffer = ReadAndDecompress(fs, rb, out read);

            if (rb.IsInternal)
            {
                // XBlock and XXBlock structures are very similar, we can use the same layout for both
                // Unicode and ANSI structures are also close, enabling us to use the same layout for them, too
                var xb = Map.MapType<XBLOCKUnicode>(buffer, 0);

                if (IsUnicode)
                {
                    var rgbid = Map.MapArray<UInt64>(buffer, Marshal.SizeOf(typeof(XBLOCKUnicode)), xb.cEnt);

                    for (int i = 0; i < rgbid.Length; i++)
                    {
                        // Recurse. XBlock and XXBlock can have common handling
                        // Pass what we know here of the total length through, so that it can be returned on the first block
                        foreach (var buf in ReadDataBlocksInternal(fs, rgbid[i], totalLength != 0 ? totalLength : xb.lcbTotal))
                            yield return buf;
                    }
                }
                else
                {
                    // The ANSI difference is that IDs are only 32 bits
                    var rgbid = Map.MapArray<UInt32>(buffer, Marshal.SizeOf(typeof(XBLOCKUnicode)), xb.cEnt);

                    for (int i = 0; i < rgbid.Length; i++)
                    {
                        // Recurse. XBlock and XXBlock can have common handling
                        // Pass what we know here of the total length through, so that it can be returned on the first block
                        foreach (var buf in ReadDataBlocksInternal(fs, rgbid[i], totalLength != 0 ? totalLength : xb.lcbTotal))
                            yield return buf;
                    }
                }
            }
            else
            {
                // Key for cyclic algorithm is the low 32 bits of the BID, so supply it in case it's needed
                Decrypt(ref buffer, (UInt32)(dataBid & 0xffffffff));

                yield return buffer;
            }
        }

        // Return all the data blocks referenced by a particular data Id, concatenating them into a single buffer
        private byte[] ReadDataBlockInternal(FileStream fs, UInt64 dataBid, ref int offset, byte[] buffer = null)
        {
            bool first = (buffer == null);  // Remember if we're at the top of a potential recursion
            var rb = LookupDataBlock(dataBid);
            if (rb == null)
                throw new XstException("Data block does not exist");
            if (first)
                offset = 0;

            // First guy in allocates enough to hold the initial block
            // This is either the one and only block, or gets replaced when we find out how much data there is in total
            int read;
            buffer = ReadAndDecompress(fs, rb, out read, buffer, offset);

            if (rb.IsInternal)
            {
                // XBlock and XXBlock structures are very similar, we can use the same layout for both
                // Unicode and ANSI structures are also close, enabling us to use the same layout for them, too
                var xb = Map.MapType<XBLOCKUnicode>(buffer, offset);

                if (IsUnicode)
                {
                    var rgbid = Map.MapArray<UInt64>(buffer, offset + Marshal.SizeOf(typeof(XBLOCKUnicode)), xb.cEnt);

                    if (first)
                    {
                        // First block, allocate a buffer big enough to hold all the data
                        buffer = new byte[xb.lcbTotal];
                        offset = 0;
                    }

                    for (int i = 0; i < rgbid.Length; i++)
                    {
                        // Recurse. XBlock and XXBlock can have common handling
                        ReadDataBlockInternal(fs, rgbid[i], ref offset, buffer);
                    }
                }
                else
                {
                    // The ANSI difference is that IDs are only 32 bits 
                    var rgbid = Map.MapArray<UInt32>(buffer, offset + Marshal.SizeOf(typeof(XBLOCKUnicode)), xb.cEnt);

                    if (first)
                    {
                        // First block, allocate a buffer big enough to hold all the data
                        buffer = new byte[xb.lcbTotal];
                        offset = 0;
                    }

                    for (int i = 0; i < rgbid.Length; i++)
                    {
                        // Recurse. XBlock and XXBlock can have common handling
                        ReadDataBlockInternal(fs, rgbid[i], ref offset, buffer);
                    }
                }
            }
            else
            {
                // Key for cyclic algorithm is the low 32 bits of the BID
                // Assume that this means the BID of each block, rather than all using the BID from the head of the tree
                Decrypt(ref buffer, (UInt32)(dataBid & 0xffffffff), offset, read);

                // Increment the offset by the length of the real data that we have read
                offset += read;
            }

            if (first)
            {
                // The recursion is over, check the results
                if (offset != buffer.Length)
                    throw new XstException("Data xblock length mismatch");

                return buffer;
            }
            else
                return null;  // Value only returned from the top level guy
        }

        private byte[] ReadAndDecompress(FileStream fs, DataRef rb, out int read, byte[] buffer = null, int offset = 0)
        {
            if (rb == null)
                throw new XstException("Data block does not exist");

            var trailerLength = IsUnicode4K
                ? Marshal.SizeOf(typeof(BLOCKTRAILERUnicode4K))
                : IsUnicode
                    ? Marshal.SizeOf(typeof(BLOCKTRAILERUnicode))
                    : Marshal.SizeOf(typeof(BLOCKTRAILERANSI));
            var blockSize = Integrity.AlignTo64(checked((int)rb.Length + trailerLength));
            var blockBytes = new byte[blockSize];
            fs.Seek((long)rb.Offset, SeekOrigin.Begin);
            fs.ReadExactly(blockBytes, 0, blockBytes.Length);

            if (IsUnicode4K && rb.Length != rb.InflatedLength)
            {
                // The first two bytes are a zlib header which DeflateStream does not understand
                // They should be 0x789c, the magic code for default compression
                if (blockBytes[0] != 0x78 || blockBytes[1] != 0x9c)
                    throw new XstException("Unexpected header in compressed data stream");

                using (var compressedStream = new MemoryStream(blockBytes, 2, checked((int)rb.Length) - 2, false))
                using (DeflateStream decompressionStream = new DeflateStream(compressedStream, CompressionMode.Decompress, false))
                {
                    if (buffer == null)
                        buffer = new byte[rb.InflatedLength];

                    decompressionStream.ReadExactly(buffer, offset, checked((int)rb.InflatedLength));
                }
                read = rb.InflatedLength;
            }
            else
            {
                if (buffer == null)
                    buffer = new byte[rb.Length];
                Array.Copy(blockBytes, 0, buffer, offset, checked((int)rb.Length));
                ValidateBlock(blockBytes, rb.Offset, checked((int)rb.Length), IsUnicode, rb.Key, buffer, offset);
                read = rb.Length;
            }

            // To actually get the block trailer, we would need to skip to the next 64 byte boundary, minus size of trailer
            //var t = Map.ReadType<BLOCKTRAILERUnicode>(fs);

            return buffer;
        }

        // When a data block has a subnode, it can be a simple node, or a two-level tree
        // This reads a sub node and builds suitable data structures, so that we can later access data held in it
        private void ReadSubNodeBtreeUnicode(FileStream fs, UInt64 subDataBid, TreeIntermediate parent)
        {
            var rb = LookupDataBlock(subDataBid);
            if (rb == null)
                throw new XstException("SubNode data block does not exist");

            int read;
            byte[] buffer = ReadAndDecompress(fs, rb, out read);
            var sl = Map.MapType<SLBLOCKUnicode>(buffer);

            if (sl.cLevel > 0)
            {
                var rgbid = Map.MapArray<SIENTRYUnicode>(buffer, Marshal.SizeOf(typeof(SLBLOCKUnicode)), sl.cEnt);

                foreach (var sie in rgbid)
                {
                    var inter = new TreeIntermediate { Key = (sie.nid & 0xffffffff) };
                    parent.Children.Add(inter);

                    // Read child nodes in tree
                    ReadSubNodeBtreeUnicode(fs, sie.bid, inter);
                }
            }
            else
            {
                var rgbid = Map.MapArray<SLENTRYUnicode>(buffer, Marshal.SizeOf(typeof(SLBLOCKUnicode)), sl.cEnt);

                foreach (var sle in rgbid)
                {
                    // Only use low order dword of nid
                    var nb = new Node { Key = (sle.nid & 0xffffffff), DataBid = sle.bidData, SubDataBid = sle.bidSub };
                    parent.Children.Add(nb);
                }
            }
        }

        private void ReadSubNodeBtreeANSI(FileStream fs, UInt64 subDataBid, TreeIntermediate parent)
        {
            var rb = LookupDataBlock(subDataBid);
            if (rb == null)
                throw new XstException("SubNode data block does not exist");

            int read;
            byte[] buffer = ReadAndDecompress(fs, rb, out read);
            var sl = Map.MapType<SLBLOCKANSI>(buffer);

            if (sl.cLevel > 0)
            {
                var rgbid = Map.MapArray<SIENTRYANSI>(buffer, Marshal.SizeOf(typeof(SLBLOCKANSI)), sl.cEnt);

                foreach (var sie in rgbid)
                {
                    var inter = new TreeIntermediate { Key = (sie.nid & 0xffffffff) };
                    parent.Children.Add(inter);

                    // Read child nodes in tree
                    ReadSubNodeBtreeANSI(fs, sie.bid, inter);
                }
            }
            else
            {
                var rgbid = Map.MapArray<SLENTRYANSI>(buffer, Marshal.SizeOf(typeof(SLBLOCKANSI)), sl.cEnt);

                foreach (var sle in rgbid)
                {
                    var nb = new Node { Key = sle.nid, DataBid = sle.bidData, SubDataBid = sle.bidSub };
                    parent.Children.Add(nb);
                }
            }
        }

        private void ValidateUnicodeHeader(byte[] header1Bytes, byte[] header2Bytes)
        {
            var headerBytes = new byte[header1Bytes.Length + header2Bytes.Length];
            Array.Copy(header1Bytes, 0, headerBytes, 0, header1Bytes.Length);
            Array.Copy(header2Bytes, 0, headerBytes, header1Bytes.Length, header2Bytes.Length);

            var h1 = Map.MapType<FileHeader1>(header1Bytes);
            var h2 = Map.MapType<FileHeader2Unicode>(header2Bytes);
            if (h2.bSentinel != 0x80)
                throw new XstException("Unicode OST/PST header sentinel is invalid");
            if (Integrity.ComputeCrc(headerBytes, 8, 471) != h1.dwCRCPartial)
                throw new XstException("Unicode OST/PST header partial CRC is invalid");
            if (Integrity.ComputeCrc(headerBytes, 8, 516) != h2.dwCRCFull)
                throw new XstException("Unicode OST/PST header full CRC is invalid");
        }

        private void ValidateAnsiHeader(byte[] header1Bytes, byte[] header2Bytes)
        {
            var headerBytes = new byte[header1Bytes.Length + header2Bytes.Length];
            Array.Copy(header1Bytes, 0, headerBytes, 0, header1Bytes.Length);
            Array.Copy(header2Bytes, 0, headerBytes, header1Bytes.Length, header2Bytes.Length);

            var h1 = Map.MapType<FileHeader1>(header1Bytes);
            var h2 = Map.MapType<FileHeader2ANSI>(header2Bytes);
            if (h2.bSentinel != 0x80)
                throw new XstException("ANSI OST/PST header sentinel is invalid");
            if (Integrity.ComputeCrc(headerBytes, 8, 471) != h1.dwCRCPartial)
                throw new XstException("ANSI OST/PST header partial CRC is invalid");
        }

        private void ValidatePage(byte[] pageBytes, ulong fileOffset, bool isUnicode)
        {
            if (isUnicode)
            {
                var page = Map.MapType<BTPAGEUnicode>(pageBytes);
                if (page.pageTrailer.ptype != page.pageTrailer.ptypeRepeat)
                    throw new XstException("Unicode page trailer type mismatch");
                if (Integrity.ComputeCrc(pageBytes, 0, pageBytes.Length - Marshal.SizeOf(typeof(PAGETRAILERUnicode))) != page.pageTrailer.dwCRC)
                    throw new XstException("Unicode page CRC is invalid");
                if (page.pageTrailer.wSig != ComputePageSignature(fileOffset, page.pageTrailer.ptype, page.pageTrailer.bid))
                    throw new XstException("Unicode page signature is invalid");
            }
            else
            {
                var page = Map.MapType<BTPAGEANSI>(pageBytes);
                if (page.pageTrailer.ptype != page.pageTrailer.ptypeRepeat)
                    throw new XstException("ANSI page trailer type mismatch");
                if (Integrity.ComputeCrc(pageBytes, 0, pageBytes.Length - Marshal.SizeOf(typeof(PAGETRAILERANSI))) != page.pageTrailer.dwCRC)
                    throw new XstException("ANSI page CRC is invalid");
                if (page.pageTrailer.wSig != ComputePageSignature(fileOffset, page.pageTrailer.ptype, page.pageTrailer.bid))
                    throw new XstException("ANSI page signature is invalid");
            }
        }

        private ushort ComputePageSignature(ulong fileOffset, Eptype pageType, ulong bid)
        {
            if (pageType == Eptype.ptypeFMap || pageType == Eptype.ptypePMap || pageType == Eptype.ptypeAMap || pageType == Eptype.ptypeFPMap)
                return 0x0000;

            return Integrity.ComputeSignature(fileOffset, bid);
        }

        private void ValidateBlock(byte[] blockBytes, ulong fileOffset, int dataLength, bool isUnicode, ulong bid, byte[] buffer, int offset)
        {
            if (isUnicode)
            {
                var trailerOffset = blockBytes.Length - Marshal.SizeOf(typeof(BLOCKTRAILERUnicode));
                var trailer = Map.MapType<BLOCKTRAILERUnicode>(blockBytes, trailerOffset);
                if (trailer.cb != dataLength)
                    throw new XstException("Unicode block length trailer is invalid");
                if (trailer.bid != bid)
                    throw new XstException("Unicode block BID trailer is invalid");
                if (trailer.wSig != Integrity.ComputeSignature(fileOffset, trailer.bid))
                    throw new XstException("Unicode block signature is invalid");

                if (Integrity.ComputeCrc(buffer, offset, dataLength) != trailer.dwCRC)
                    throw new XstException("Unicode block CRC is invalid");
            }
            else
            {
                var trailerOffset = blockBytes.Length - Marshal.SizeOf(typeof(BLOCKTRAILERANSI));
                var trailer = Map.MapType<BLOCKTRAILERANSI>(blockBytes, trailerOffset);
                if (trailer.cb != dataLength)
                    throw new XstException("ANSI block length trailer is invalid");
                if (trailer.bid != (UInt32)bid)
                    throw new XstException("ANSI block BID trailer is invalid");
                if (trailer.wSig != Integrity.ComputeSignature(fileOffset, trailer.bid))
                    throw new XstException("ANSI block signature is invalid");

                if (Integrity.ComputeCrc(buffer, offset, dataLength) != trailer.dwCRC)
                    throw new XstException("ANSI block CRC is invalid");
            }
        }
        #endregion
    }
}
