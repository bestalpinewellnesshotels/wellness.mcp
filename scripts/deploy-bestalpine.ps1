#!/usr/bin/env powershell
# Legacy-Wrapper: bitte DEPLOY-PRODUCTION.ps1 verwenden.
& (Join-Path (Split-Path $PSScriptRoot -Parent) "DEPLOY-PRODUCTION.ps1") @args
