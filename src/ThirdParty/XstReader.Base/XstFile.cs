// Copyright (c) 2016, Dijji, and released under Ms-PL.  This can be found in the root of this distribution. 

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;



namespace XstReader
{
    // Main handling for xst (.ost and .pst) files
    //
    // The code here implements the messaging layer, which depends on and invokes the NDP and LTP layers
    //
    // The constructor provides the path to the file, and then there are just a few public methods:
    // - Read the structure of the folders
    // - Read the list of messages contained in a folder
    // - Read the contents of a message
    // - Save an attachment to a message

    public class XstFile
    {
        private NDB ndb;
        private LTP ltp;

        private static string AsString(object value) => (string)value;
        private static uint AsUInt32(object value) => value switch
        {
            uint uintValue => uintValue,
            int intValue when intValue >= 0 => checked((uint)intValue),
            short shortValue when shortValue >= 0 => checked((uint)shortValue),
            ushort ushortValue => ushortValue,
            byte byteValue => byteValue,
            sbyte sbyteValue when sbyteValue >= 0 => checked((uint)sbyteValue),
            _ => throw new InvalidCastException($"Unable to cast object of type '{value?.GetType()}' to UInt32.")
        };
        private static int AsInt32(object value) => value switch
        {
            int intValue => intValue,
            uint uintValue when uintValue <= int.MaxValue => checked((int)uintValue),
            short shortValue => shortValue,
            ushort ushortValue => ushortValue,
            byte byteValue => byteValue,
            sbyte sbyteValue => sbyteValue,
            _ => throw new InvalidCastException($"Unable to cast object of type '{value?.GetType()}' to Int32.")
        };
        private static bool AsBool(object value) => (bool)value;
        private static DateTime? AsNullableDateTime(object value) => (DateTime?)value;
        private static byte[] AsBytes(object value) => (byte[])value;
        private static MessageFlags AsMessageFlags(object value) => (MessageFlags)AsInt32(value);
        private static BodyType AsBodyType(object value) => (BodyType)AsInt32(value);
        private static RecipientType AsRecipientType(object value) => (RecipientType)AsInt32(value);
        private static AttachMethods AsAttachMethod(object value) => (AttachMethods)AsInt32(value);
        private static AttachFlags AsAttachFlags(object value) => (AttachFlags)AsUInt32(value);

        // We use sets of PropertyGetters to define the equivalent of queries when reading property sets and tables

        // The folder properties we read when exploring folder structure
        private static readonly PropertyGetters<Folder> pgFolder = new PropertyGetters<Folder>
        {
            {EpropertyTag.PidTagDisplayName, (f, val) => f.Name = AsString(val) },
            {EpropertyTag.PidTagContentCount, (f, val) => f.ContentCount = AsUInt32(val) },
            // Don't bother reading HasSubFolders, because it is not always set
            // {EpropertyTag.PidTagSubfolders, (f, val) => f.HasSubFolders = val },
        };

        // When reading folder contents, the message properties we ask for
        private static readonly PropertyGetters<Message> pgMessageList = new PropertyGetters<Message>
        {
            {EpropertyTag.PidTagSubjectW, (m, val) => m.Subject = AsString(val) },
            {EpropertyTag.PidTagDisplayCcW, (m, val) => m.Cc = AsString(val) },
            {EpropertyTag.PidTagDisplayToW, (m, val) => m.To = AsString(val) },
            {EpropertyTag.PidTagMessageFlags, (m, val) => m.Flags = AsMessageFlags(val) },
            {EpropertyTag.PidTagSentRepresentingNameW, (m, val) => m.From = AsString(val) },
            {EpropertyTag.PidTagClientSubmitTime, (m, val) => m.Submitted = AsNullableDateTime(val) },
            {EpropertyTag.PidTagMessageDeliveryTime, (m, val) => m.Received = AsNullableDateTime(val) },
            {EpropertyTag.PidTagLastModificationTime, (m, val) => m.Modified = AsNullableDateTime(val) },
        };

        // When reading folder contents, the message properties we ask for
        // In Unicode4K, PidTagSentRepresentingNameW doesn't yield a useful value
        private static readonly PropertyGetters<Message> pgMessageList4K = new PropertyGetters<Message>
        {
            {EpropertyTag.PidTagSubjectW, (m, val) => m.Subject = AsString(val) },
            {EpropertyTag.PidTagDisplayCcW, (m, val) => m.Cc = AsString(val) },
            {EpropertyTag.PidTagDisplayToW, (m, val) => m.To = AsString(val) },
            {EpropertyTag.PidTagMessageFlags, (m, val) => m.Flags = AsMessageFlags(val) },
            {EpropertyTag.PidTagClientSubmitTime, (m, val) => m.Submitted = AsNullableDateTime(val) },
            {EpropertyTag.PidTagMessageDeliveryTime, (m, val) => m.Received = AsNullableDateTime(val) },
            {EpropertyTag.PidTagLastModificationTime, (m, val) => m.Modified = AsNullableDateTime(val) },
        };

        private static readonly PropertyGetters<Message> pgMessageDetail4K = new PropertyGetters<Message>
        {
            {EpropertyTag.PidTagSentRepresentingNameW, (m, val) => m.From = AsString(val) },
            {EpropertyTag.PidTagSentRepresentingEmailAddress, (m, val) => { if(m.From == null) m.From = AsString(val); } },
            {EpropertyTag.PidTagSenderName, (m, val) => { if(m.From == null) m.From = AsString(val); } },
        };

        // The properties we read when accessing the contents of a message
        private static readonly PropertyGetters<Message> pgMessageContent = new PropertyGetters<Message>
        {
            {EpropertyTag.PidTagNativeBody, (m, val) => m.NativeBody = AsBodyType(val) },
            {EpropertyTag.PidTagBody, (m, val) => m.Body = AsString(val) },
            //{EpropertyTag.PidTagInternetCodepage, (m, val) => m.InternetCodePage = (int)val },
            // In ANSI format, PidTagHtml is called PidTagBodyHtml (though the tag code is the same), because it is a string rather than a binary value
            // Here, we test the type to determine where to put the value 
            {EpropertyTag.PidTagHtml, (m, val) => { if (val is string htmlText)  m.BodyHtml = htmlText; else m.Html = AsBytes(val); } },
            {EpropertyTag.PidTagRtfCompressed, (m, val) => m.RtfCompressed = AsBytes(val) },
        };

        // The properties we read when accessing the recipient table of a message
        private static readonly PropertyGetters<Recipient> pgMessageRecipient = new PropertyGetters<Recipient>
        {
            {EpropertyTag.PidTagRecipientType, (r, val) => r.RecipientType = AsRecipientType(val) },
            {EpropertyTag.PidTagDisplayName, (r, val) => r.DisplayName = AsString(val) },
            {EpropertyTag.PidTagEmailAddress, (r, val) => r.EmailAddress = AsString(val) },
        };

        //The properties we read when accessing a message attached to a message
        private static readonly PropertyGetters<Message> pgMessageAttachment = new PropertyGetters<Message>
        {
            {EpropertyTag.PidTagSubjectW, (m, val) => m.Subject = AsString(val) },
            {EpropertyTag.PidTagDisplayCcW, (m, val) => m.Cc = AsString(val) },
            {EpropertyTag.PidTagDisplayToW, (m, val) => m.To = AsString(val) },
            {EpropertyTag.PidTagMessageFlags, (m, val) => m.Flags = AsMessageFlags(val) },
            {EpropertyTag.PidTagSentRepresentingNameW, (m, val) => m.From = AsString(val) },
            {EpropertyTag.PidTagClientSubmitTime, (m, val) => m.Submitted = AsNullableDateTime(val) },
            {EpropertyTag.PidTagMessageDeliveryTime, (m, val) => m.Received = AsNullableDateTime(val) },
            {EpropertyTag.PidTagLastModificationTime, (m, val) => m.Modified = AsNullableDateTime(val) },
            {EpropertyTag.PidTagNativeBody, (m, val) => m.NativeBody = AsBodyType(val) },
            {EpropertyTag.PidTagBody, (m, val) => m.Body = AsString(val) },
            {EpropertyTag.PidTagHtml, (m, val) => { if (val is string htmlText)  m.BodyHtml = htmlText; else m.Html = AsBytes(val); } },
            {EpropertyTag.PidTagRtfCompressed, (m, val) => m.RtfCompressed = AsBytes(val) },
        };

        private static readonly HashSet<EpropertyTag> contentExclusions = new HashSet<EpropertyTag>
        {
            EpropertyTag.PidTagNativeBody,
            EpropertyTag.PidTagBody,
            EpropertyTag.PidTagHtml,
            EpropertyTag.PidTagRtfCompressed,
        };

        // The properties we read when getting a list of attachments
        private static readonly PropertyGetters<Attachment> pgAttachmentList = new PropertyGetters<Attachment>
        {
            {EpropertyTag.PidTagDisplayName, (a, val) => a.DisplayName = AsString(val) },
            {EpropertyTag.PidTagAttachFilenameW, (a, val) => a.FileNameW = AsString(val) },
            {EpropertyTag.PidTagAttachLongFilename, (a, val) => a.LongFileName = AsString(val) },
            {EpropertyTag.PidTagAttachmentSize, (a, val) => a.Size = AsInt32(val) },
            {EpropertyTag.PidTagAttachMethod, (a, val) => a.AttachMethod = AsAttachMethod(val) },
            //{EpropertyTag.PidTagAttachMimeTag, (a, val) => a.MimeTag = val },
            {EpropertyTag.PidTagAttachPayloadClass, (a, val) => a.FileNameW = AsString(val) },
        };

        // The properties we read To enable handling of HTML images delivered as attachments
        private static readonly PropertyGetters<Attachment> pgAttachedHtmlImages = new PropertyGetters<Attachment>
        {
            {EpropertyTag.PidTagAttachFlags, (a, val) => a.Flags = AsAttachFlags(val) },
            {EpropertyTag.PidTagAttachMimeTag, (a, val) => a.MimeTag = AsString(val) },
            {EpropertyTag.PidTagAttachContentId, (a, val) => a.ContentId = AsString(val) },
            {EpropertyTag.PidTagAttachmentHidden, (a, val) => a.Hidden = AsBool(val) },
        };

        // The properties we read when accessing the name of an attachment
        private static readonly PropertyGetters<Attachment> pgAttachmentName = new PropertyGetters<Attachment>
        {
            {EpropertyTag.PidTagAttachFilenameW, (a, val) => a.FileNameW = AsString(val) },
            {EpropertyTag.PidTagAttachLongFilename, (a, val) => a.LongFileName = AsString(val) },
        };

        // The properties we read when accessing the contents of an attachment
        private static readonly PropertyGetters<Attachment> pgAttachmentContent = new PropertyGetters<Attachment>
        {
            {EpropertyTag.PidTagAttachDataBinary, (a, val) => a.Content = val },
        };

        private static readonly HashSet<EpropertyTag> attachmentContentExclusions = new HashSet<EpropertyTag>
        {
            EpropertyTag.PidTagAttachDataBinary,
        };


        #region Public methods

        public XstFile(string fullName)
        {
            this.ndb = new NDB(fullName);
            this.ltp = new LTP(ndb);
        }

        public Folder ReadFolderTree()
        {
            ndb.Initialise();

            using (var fs = ndb.GetReadStream())
            {
                return ReadFolderStructure(fs, new NID(EnidSpecial.NID_ROOT_FOLDER));
            }
        }

        public List<Message> ReadMessages(Folder f)
        {
            f.Messages.Clear();
            if (f.ContentCount > 0)
            {
                using (var fs = ndb.GetReadStream())
                {
                    // Get the Contents table for the folder
                    // For 4K, not all the properties we want are available in the Contents table, so supplement them from the Message itself
                    var ms = ltp.ReadTable<Message>(fs, NID.TypedNID(EnidType.CONTENTS_TABLE, f.Nid),
                                                    ndb.IsUnicode4K ? pgMessageList4K : pgMessageList, (m, id) => m.Nid = new NID(id))
                                .Select(m => ndb.IsUnicode4K ? Add4KMessageProperties(fs, m) : m)
                                .Select(m => f.AddMessage(m))
                                .ToList(); // to force complete execution on the current thread
                    return ms;
                }
            }
            return new List<Message>();
        }

        public void ReadMessageDetails(Message m)
        {
            using (var fs = ndb.GetReadStream())
            {
                // Read the contents properties
                var subNodeTree = ltp.ReadProperties<Message>(fs, m.Nid, pgMessageContent, m);

                // Read all other properties
                m.Properties.Clear();
                m.Properties.AddRange(ltp.ReadAllProperties(fs, m.Nid, contentExclusions).ToList());

                ReadMessageTables(fs, subNodeTree, m);
            }
        }

        public List<Property> ReadAttachmentProperties(Attachment a)
        {
            using (var fs = ndb.GetReadStream())
            {
                BTree<Node> subNodeTreeMessage = a.subNodeTreeProperties;

                if (subNodeTreeMessage == null)
                    // No subNodeTree given: assume we can look it up in the main tree
                    ndb.LookupNodeAndReadItsSubNodeBtree(fs, a.Message.Nid, out subNodeTreeMessage);

                // Read all non-content properties
                // Convert to list so that we can dispose the file access
                return new List<Property>(ltp.ReadAllProperties(fs, subNodeTreeMessage, a.Nid, attachmentContentExclusions, true));
            }
        }

        public void SaveVisibleAttachmentsToAssociatedFolder(string fullFileName, Message m)
        {
            if (m.HasVisibleFileAttachment)
            {
                var targetFolder = Path.Combine(Path.GetDirectoryName(fullFileName),
                    Path.GetFileNameWithoutExtension(fullFileName) + " Attachments");
                if (!Directory.Exists(targetFolder))
                {
                    Directory.CreateDirectory(targetFolder);
                    if (m.Date != null)
                        SetDirectoryTimestamp(targetFolder, (DateTime)m.Date);
                }
                SaveAttachmentsToFolder(targetFolder, m.Date, m.Attachments.Where(a => a.IsFile && !a.Hide));
            }
        }

        public void SaveAttachmentsToFolder(string fullFolderName, DateTime? creationTime, IEnumerable<Attachment> attachments)
        {
            foreach (var a in attachments)
            {
                SaveAttachmentToFolder(fullFolderName, creationTime, a);
            }
        }

        const int MaxPath = 260;
        public void SaveAttachmentToFolder(string folderpath, DateTime? creationTime, Attachment a)
        {
            var fullFileName = Path.Combine(folderpath, a.FileName);

            // If the result is too long, truncate the attachment name as required
            if (fullFileName.Length >= MaxPath)
            {
                var ext = Path.GetExtension(a.FileName);
                var att = Path.GetFileNameWithoutExtension(a.FileName)
                    .Truncate(MaxPath - folderpath.Length - ext.Length - 5) + ext;
                fullFileName = Path.Combine(folderpath, att);
            }
            SaveAttachment(fullFileName, creationTime, a);
        }

        public void SaveAttachment(string fullFileName, DateTime? creationTime, Attachment a)
        {
            using (var afs = new FileStream(fullFileName, FileMode.Create, FileAccess.Write))
            {
                SaveAttachment(afs, a);
            }
            if (creationTime != null)
                SetFileTimestamp(fullFileName, (DateTime)creationTime);
        }

        public void SaveAttachment(Stream s, Attachment a)
        {
            if (a.WasLoadedFromMime)
            {
                var contentBytes = (byte[])a.Content;
                s.Write(contentBytes, 0, contentBytes.Length);
            }
            else
            {
                using (FileStream fs = ndb.GetReadStream())
                {
                    BTree<Node> subNodeTreeMessage = a.subNodeTreeProperties;

                    if (subNodeTreeMessage == null)
                        // No subNodeTree given: assume we can look it up in the main tree
                        ndb.LookupNodeAndReadItsSubNodeBtree(fs, a.Message.Nid, out subNodeTreeMessage);

                    var subNodeTreeAttachment = ltp.ReadProperties<Attachment>(fs, subNodeTreeMessage, a.Nid, pgAttachmentContent, a);

                    if (a.Content != null)
                    {
                        // If the value is inline, we just write it out
                        if (a.Content is byte[] contentBytes)
                        {
                            s.Write(contentBytes, 0, contentBytes.Length);
                        }
                        // Otherwise we need to dereference the node pointing to the data,
                        // using the subnode tree belonging to the attachment
                        else if (a.Content is NID contentNid)
                        {
                            var nb = NDB.LookupSubNode(subNodeTreeAttachment, contentNid);

                            // Copy the data to the output file stream without getting it all into memory at once,
                            // as there can be a lot of data
                            ndb.CopyDataBlocks(fs, s, nb.DataBid);
                        }
                    }
                }
            }
        }

        public Message OpenAttachedMessage(Attachment a)
        {

            using (FileStream fs = ndb.GetReadStream())
            {
                BTree<Node> subNodeTreeMessage = a.subNodeTreeProperties;

                if (subNodeTreeMessage == null)
                    // No subNodeTree given: assume we can look it up in the main tree
                    ndb.LookupNodeAndReadItsSubNodeBtree(fs, a.Message.Nid, out subNodeTreeMessage);

                var subNodeTreeAttachment = ltp.ReadProperties<Attachment>(fs, subNodeTreeMessage, a.Nid, pgAttachmentContent, a);
                if (a.Content is PtypObjectValue objectValue)
                {
                    Message m = new Message { Nid = new NID(objectValue.Nid) };

                    // Read the basic and contents properties
                    var childSubNodeTree = ltp.ReadProperties<Message>(fs, subNodeTreeAttachment, m.Nid, pgMessageAttachment, m, true);

                    // Read all other properties
                    m.Properties.AddRange(ltp.ReadAllProperties(fs, subNodeTreeAttachment, m.Nid, contentExclusions, true).ToList());

                    ReadMessageTables(fs, childSubNodeTree, m, true);

                    return m;
                }
                else
                    throw new XstException("Unexpected data type for attached message");
            }
        }


        private struct LineProp
        {
            public int line;
            public Property p;
        }

        public void ExportMessageProperties(IEnumerable<Message> messages, string fileName)
        {
            // We build a dictionary of queues of line,Property pairs where each queue represents
            // a column in the CSV file, and the line is the line number in the file.
            // The key to the dictionary is the property ID.

            var dict = new Dictionary<string, Queue<LineProp>>();
            int lines = 1;

            foreach (var m in messages)
            {
                //// Do not reread properties for current message as it will fail updating the display
                //if (m != view.CurrentMessage)
                {
                    try
                    {
                        ReadMessageDetails(m);
                    }
                    catch (XstException)
                    {
                        // Ignore file exceptions to get as much as we can
                    }
                }


                foreach (var p in m.Properties)
                {
                    Queue<LineProp> queue;
                    if (!dict.TryGetValue(p.CsvId, out queue))
                    {
                        queue = new Queue<LineProp>();
                        dict[p.CsvId] = queue;
                    }
                    queue.Enqueue(new LineProp { line = lines, p = p });
                }
                lines++;
            }

            // Now we sort the columns by ID
            var columns = dict.Keys.OrderBy(x => x).ToArray();

            // And finally output the CSV file line by line
            using (var sw = new System.IO.StreamWriter(fileName, false, Encoding.UTF8))

            {
                StringBuilder sb = new StringBuilder();
                bool hasValue = false;

                for (int line = 0; line < lines; line++)
                {
                    foreach (var col in columns)
                    {
                        var q = dict[col];

                        // First line is always the column headers
                        if (line == 0)
                            AddCsvValue(sb, q.Peek().p.CsvDescription, ref hasValue);

                        // After that, output the column value if it has one
                        else if (q.Count > 0 && q.Peek().line == line)
                            AddCsvValue(sb, q.Dequeue().p.DisplayValue, ref hasValue);
                        
                        // Or leave it blank
                        else
                            AddCsvValue(sb, "", ref hasValue);
                    }

                    // Write the completed line out
                    sw.WriteLine(sb.ToString());
                    sb.Clear();
                    hasValue = false;
                }
            }
        }
        #endregion

        #region Private methods

        // Recurse down the folder tree, building a structure of Folder classes
        private Folder ReadFolderStructure(FileStream fs, NID nid, Folder parentFolder = null)
        {
            Folder f = new Folder { Nid = nid, XstFile = this, ParentFolder = parentFolder };

            ltp.ReadProperties<Folder>(fs, nid, pgFolder, f);

            f.Folders.AddRange(ltp.ReadTableRowIds(fs, NID.TypedNID(EnidType.HIERARCHY_TABLE, nid))
                                  .Where(id => id.nidType == EnidType.NORMAL_FOLDER)
                                  .Select(id => ReadFolderStructure(fs, id, f))
                                  .OrderBy(sf => sf.Name)
                                  .ToList());
            return f;
        }

        private Message Add4KMessageProperties(FileStream fs, Message m)
        {
            ltp.ReadProperties<Message>(fs, m.Nid, pgMessageDetail4K, m);

            return m;
        }

        private void ReadMessageTables(FileStream fs, BTree<Node> subNodeTree, Message m, bool isAttached = false)
        {
            // Read the recipient table for the message
            var recipientsNid = new NID(EnidSpecial.NID_RECIPIENT_TABLE);
            if (ltp.IsTablePresent(subNodeTree, recipientsNid))
            {
                var rs = ltp.ReadTable<Recipient>(fs, subNodeTree, recipientsNid, pgMessageRecipient, null, (r, p) => r.Properties.Add(p));
                m.Recipients.Clear();
                foreach (var r in rs)
                {
                    // Sort the properties
                    List<Property> lp = new List<Property>(r.Properties);
                    lp.Sort((a, b) => a.Tag.CompareTo(b.Tag));
                    r.Properties.Clear();
                    foreach (var p in lp)
                        r.Properties.Add(p);

                    m.Recipients.Add(r);
                }
            }

            // Read any attachments
            var attachmentsNid = new NID(EnidSpecial.NID_ATTACHMENT_TABLE);
            if (m.HasAttachment)
            {
                if (!ltp.IsTablePresent(subNodeTree, attachmentsNid))
                    throw new XstException("Could not find expected Attachment table");

                // Read the attachment table, which is held in the subnode of the message
                var atts = ltp.ReadTable<Attachment>(fs, subNodeTree, attachmentsNid, pgAttachmentList, (a, id) => a.Nid = new NID(id)).ToList();
                foreach (var a in atts)
                {
                    a.Message = m; // For lazy reading of the complete properties: a.Message.Folder.XstFile

                    // If the long name wasn't in the attachment table, go look for it in the attachment properties
                    if (a.LongFileName == null)
                        ltp.ReadProperties<Attachment>(fs, subNodeTree, a.Nid, pgAttachmentName, a);

                    // Read properties relating to HTML images presented as attachments
                    ltp.ReadProperties<Attachment>(fs, subNodeTree, a.Nid, pgAttachedHtmlImages, a);

                    // If this is an embedded email, tell the attachment where to look for its properties
                    // This is needed because the email node is not in the main node tree
                    if (isAttached)
                        a.subNodeTreeProperties = subNodeTree;
                }

                m.SaveAttachments(atts);
            }
        }

        private void AddCsvValue(StringBuilder sb, string value, ref bool hasValue)
        {
            if (hasValue)
                sb.Append(CultureInfo.CurrentCulture.TextInfo.ListSeparator); // aka comma

            if (value != null)
            {
                // Multilingual characters should be quoted, so we will just quote all values,
                // which means we need to double quotes in the value
                // Excel cannot cope with Unicode files with values containing
                // new line characters, so remove those as well
                var val = value.Replace("\"", "\"\"").Replace("\r\n", "; ").Replace("\r", " ").Replace("\n", " ");
                sb.Append("\"");
                sb.Append(EnforceCsvValueLengthLimit(val));
                sb.Append("\"");
            }

            hasValue = true;
        }

        private static int valueLengthLimit = (int)Math.Pow(2, 15) - 12;
        private string EnforceCsvValueLengthLimit(string value)
        {
            if (value.Length < valueLengthLimit)
                return value;
            else
                return value.Substring(0, valueLengthLimit) + "…";
        }

        private static void SetDirectoryTimestamp(string path, DateTime timestamp)
        {
            try
            {
                Directory.SetCreationTime(path, timestamp);
            }
            catch (PlatformNotSupportedException)
            {
                Directory.SetLastWriteTime(path, timestamp);
            }
            catch (IOException)
            {
                Directory.SetLastWriteTime(path, timestamp);
            }
        }

        private static void SetFileTimestamp(string path, DateTime timestamp)
        {
            try
            {
                File.SetCreationTime(path, timestamp);
            }
            catch (PlatformNotSupportedException)
            {
                File.SetLastWriteTime(path, timestamp);
            }
            catch (IOException)
            {
                File.SetLastWriteTime(path, timestamp);
            }
        }

        #endregion
    }

    public class XstException : Exception
    {
        public XstException(string message) : base(message)
        {
        }
    }
}
