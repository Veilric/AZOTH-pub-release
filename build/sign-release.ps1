<#
.SYNOPSIS
  AZOTH public release publishing is not configured for this source candidate.
.DESCRIPTION
  Build scripts can create local artifacts. Publishing must be configured explicitly for a verified Veilric-owned
  repository and the maintainer's own signing identity. No upstream mirrors or credentials are used.
#>
[CmdletBinding()]
param([string]$Version)

throw 'AZOTH release publishing is not configured. Build local artifacts with build/build-installer.ps1 or build/build-portable.ps1.'
