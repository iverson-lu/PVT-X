param([string] $Message)

Write-Output $Message
Write-Host "FOO=$env:FOO"
Write-Host "FOO_PLAN=$env:FOO_PLAN"
Write-Host "HP_ACCOUNT=$env:HP_ACCOUNT"
# 0 = Pass, 1 = Fail, 2 = Timeout, 3 = Error
exit 0
