param([Parameter(Mandatory)][string]$KeyPath,[string]$Manifest,[string]$Signature,[switch]$Initialize)
$ErrorActionPreference='Stop'
$entropy=[Text.Encoding]::UTF8.GetBytes('EQL Catalyst update metadata v1')
if ($Initialize) {
  if (Test-Path -LiteralPath $KeyPath) { throw 'A release key already exists; it will not be replaced.' }
  $key=[Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256'))
  $private=$key.ExportPkcs8PrivateKey()
  try {
    $protected=[Security.Cryptography.ProtectedData]::Protect($private,$entropy,[Security.Cryptography.DataProtectionScope]::CurrentUser)
    New-Item -ItemType Directory -Path (Split-Path -Parent $KeyPath) -Force | Out-Null
    [IO.File]::WriteAllBytes($KeyPath,$protected)
  } finally { [Security.Cryptography.CryptographicOperations]::ZeroMemory($private) }
} else {
  $private=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($KeyPath),$entropy,[Security.Cryptography.DataProtectionScope]::CurrentUser)
  $key=[Security.Cryptography.ECDsa]::Create()
  try { $read=0; $key.ImportPkcs8PrivateKey($private,[ref]$read) } finally { [Security.Cryptography.CryptographicOperations]::ZeroMemory($private) }
}
try {
  if ($Manifest) {
    if (!$Signature) { throw 'Specify an output signature path.' }
    $bytes=[IO.File]::ReadAllBytes($Manifest)
    $signed=$key.SignData($bytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
    [IO.File]::WriteAllText($Signature,[Convert]::ToBase64String($signed),[Text.UTF8Encoding]::new($false))
  } else { [Convert]::ToBase64String($key.ExportSubjectPublicKeyInfo()) }
} finally { $key.Dispose() }
