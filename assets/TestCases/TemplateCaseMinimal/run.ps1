param([string] $Message)

Write-Output $Message
Write-Host "FOO=$env:FOO"
# 0 = Pass, 1 = Fail, 2 = Timeout, 3 = Error
exit 0
