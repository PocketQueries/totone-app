<#
.SYNOPSIS
    Totonoe 配布用ビルドスクリプト
.DESCRIPTION
    win-x64 向けに publish し、不要ランタイムを除外して ZIP を生成する。
.EXAMPLE
    .\scripts\publish.ps1
    .\scripts\publish.ps1 -SkipBuild   # publish 済みフォルダから ZIP だけ生成
#>
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$repoRoot   = Split-Path $PSScriptRoot -Parent
$projectDir = Join-Path $repoRoot "src\OnlineMeetingRecorder"
$csproj     = Join-Path $projectDir "OnlineMeetingRecorder.csproj"
$publishDir = Join-Path $repoRoot "publish"
$distDir    = Join-Path $repoRoot "dist"

# --- 1. Publish ---
if (-not $SkipBuild) {
    Write-Host "=== dotnet publish (Release, win-x64) ===" -ForegroundColor Cyan

    # 旧 publish フォルダをクリーン
    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force
    }

    dotnet publish $csproj `
        -c Release `
        -r win-x64 `
        --self-contained false `
        -o $publishDir

    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet publish failed"
        exit 1
    }
}

# --- 2. 不要ランタイムの除去 ---
Write-Host "=== Cleaning unnecessary runtimes ===" -ForegroundColor Cyan

$runtimesDir = Join-Path $publishDir "runtimes"
if (Test-Path $runtimesDir) {
    # linux, osx, win-arm64, win-x86 を削除
    Get-ChildItem $runtimesDir -Directory | Where-Object {
        $_.Name -match "^(linux|osx)" -or $_.Name -in @("win-arm64", "win-x86")
    } | ForEach-Object {
        Write-Host "  Removing $($_.Name)..." -ForegroundColor DarkGray
        Remove-Item $_.FullName -Recurse -Force
    }

    # LLamaSharp: noavx / avx512 バリアントを削除（avx2 + cuda12 のみ残す）
    $llamaNativeDir = Join-Path $runtimesDir "win-x64\native"
    if (Test-Path $llamaNativeDir) {
        @("noavx", "avx512", "avx") | ForEach-Object {
            $variantDir = Join-Path $llamaNativeDir $_
            if (Test-Path $variantDir) {
                Write-Host "  Removing LLamaSharp variant: $_..." -ForegroundColor DarkGray
                Remove-Item $variantDir -Recurse -Force
            }
        }
    }
}

# --- 3. サイズ集計 ---
$totalSize = (Get-ChildItem $publishDir -Recurse -File | Measure-Object -Property Length -Sum).Sum
$sizeMB = [math]::Round($totalSize / 1MB, 1)
$fileCount = (Get-ChildItem $publishDir -Recurse -File).Count
Write-Host "=== Publish complete: ${sizeMB}MB, ${fileCount} files ===" -ForegroundColor Green

# --- 4. ZIP 生成 ---
Write-Host "=== Creating ZIP archive ===" -ForegroundColor Cyan

if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir | Out-Null
}

# バージョンタグ（日付ベース）
$version = Get-Date -Format "yyyyMMdd"
$zipName = "Totonoe-v${version}-win-x64.zip"
$zipPath = Join-Path $distDir $zipName

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -CompressionLevel Optimal

$zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "=== ZIP created: $zipName (${zipSize}MB) ===" -ForegroundColor Green
Write-Host "  Path: $zipPath"
