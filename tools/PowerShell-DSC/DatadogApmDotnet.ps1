Configuration DatadogApmDotnet
{
    param
    (
        # Target nodes to apply the configuration
        [string]$NodeName = 'localhost',

        # Determins whether to install the Agent
        [bool]$InstallAgent = $true,

        # Version of the Agent package to be installed
        [string]$AgentVersion = '7.27.0',

        # Determins whether to install the Tracer
        [bool]$InstallTracer = $true,

        # Version of the Tracer package to be installed
        [string]$TracerVersion = '1.26.1'
    )

    Import-DscResource -ModuleName PSDscResources -Name MsiPackage
    Import-DscResource -ModuleName PSDscResources -Name Environment

    Node $NodeName
    {
        # Agent msi installer
        if ($InstallAgent) {
            MsiPackage 'dd-agent' {
                Path      = "https://s3.amazonaws.com/ddagent-windows-stable/ddagent-cli-$AgentVersion.msi"
                ProductId = '37932A4A-628E-4409-A430-0405CDAC92CD'
                Ensure    = 'Present'
            }
        }

        # .NET Tracer msi installer
        if ($InstallTracer) {
            MsiPackage 'dd-trace-dotnet' {
                Path      = "https://github.com/DataDog/dd-trace-dotnet/releases/download/v$TracerVersion/datadog-dotnet-apm-$TracerVersion-x64.msi"
                ProductId = '...'
                Ensure    = 'Present'
            }

            Environment 'COR_PROFILER' {
                Name   = 'COR_PROFILER'
                Value  = '{846F5F1C-F9AE-4B07-969E-05C26BC060D8}'
                Ensure = 'Present'
                Path   = $false
                Target = @('Process', 'Machine')
            }

            Environment 'CORECLR_PROFILER' {
                Name   = 'CORECLR_PROFILER'
                Value  = '{846F5F1C-F9AE-4B07-969E-05C26BC060D8}'
                Ensure = 'Present'
                Path   = $false
                Target = @('Process', 'Machine')
            }
        }
    }
}
