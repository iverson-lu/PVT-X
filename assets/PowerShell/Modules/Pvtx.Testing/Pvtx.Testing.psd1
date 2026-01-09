@{
  RootModule        = 'Pvtx.Testing.psm1'
  ModuleVersion     = '1.0.0'
  GUID              = 'b8c6f1a5-6e9b-4a2e-a19b-7f7a0e6b5d2a'
  Author            = 'PVT-X'
  CompanyName       = 'PVT-X'
  Copyright         = '(c) PVT-X. All rights reserved.'
  Description       = 'Common helper functions for PVT-X Test Cases (PowerShell 7+).'
  PowerShellVersion = '7.0'
  FunctionsToExport = @('Ensure-Dir','Write-JsonFile','Normalize-Text','Write-Stdout-Compact','ParseJson', 'New-Step', 'Fail-Step')
  CmdletsToExport   = @()
  VariablesToExport = @()
  AliasesToExport   = @()
}
