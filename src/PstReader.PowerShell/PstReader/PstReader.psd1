@{
    RootModule = 'PstReader.psm1'
    ModuleVersion = '1.0.0'
    GUID = '1c2d87b9-a039-4a66-85a0-334785d38dbd'
    Author = 'PstReader contributors'
    Description = 'Read PST/OST stores, folders, calendar items, tasks, and mail without Outlook.'
    PowerShellVersion = '5.1'
    DotNetFrameworkVersion = '4.7.2'
    CompatiblePSEditions = @('Desktop', 'Core')
    TypesToProcess = @('PstReader.types.ps1xml')
    FunctionsToExport = @('Get-PstStore', 'Get-PstFolder', 'Get-PstCalendarItem', 'Get-PstTask', 'Get-PstMail')
    CmdletsToExport = @()
    VariablesToExport = @()
    AliasesToExport = @()
    PrivateData = @{
        PSData = @{
            Tags = @('PST', 'OST', 'Outlook', 'Mail', 'Calendar')
            ProjectUri = 'https://github.com/horker/pstreader'
        }
    }
}
