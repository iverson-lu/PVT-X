param(
  [string]$Message,
  [int]$ExitCode,
  [string]$SecretToken
)

Write-Output "Message: $Message"
Write-Output "Secret: $SecretToken"
exit $ExitCode
