# X-9：空 catch 棘轮（ratchet）。
#
# 背景：审阅发现全仓 138+ 处 `catch { }`，其中 Dispose / 释放路径是合法的，
# 业务路径（加载、解码、采样、UI 状态）吞错则违反 AGENTS §5。
# 逐个改写风险高（AGENTS §4.1 明确禁止批量合并/删除空 catch），
# 所以这里做“只降不升”的棘轮：
#   - 计数全仓 src/**/*.cs 里的空 catch 块；
#   - 超过 tools/empty-catch-baseline.txt 的数量就让 CI 失败；
#   - 新增代码请用 SafeRelease.Run(...) 或带日志的 catch，不要新增空 catch。
# 修复一批后，把基线数字调小并提交（棘轮只会向下走）。

param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [string]$BaselineFile = (Join-Path $PSScriptRoot 'empty-catch-baseline.txt')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $BaselineFile)) {
    Write-Error "缺少空 catch 基线文件: $BaselineFile"
    exit 2
}

$baselineText = (Get-Content -LiteralPath $BaselineFile -Raw).Trim()
$baseline = 0
if (-not [int]::TryParse($baselineText, [ref]$baseline)) {
    Write-Error "基线文件内容必须是整数，实际: '$baselineText'"
    exit 2
}

$srcDir = Join-Path $Root 'src'
if (-not (Test-Path -LiteralPath $srcDir)) {
    Write-Error "找不到 src 目录: $srcDir"
    exit 2
}

# 匹配 catch / catch (Exception) / catch (Exception ex) 且方法体为空（允许空白与换行）。
$pattern = 'catch\s*(\([^)]*\))?\s*\{\s*\}'

$counts = @{}
$total = 0
Get-ChildItem -LiteralPath $srcDir -Recurse -Filter *.cs -File | ForEach-Object {
    $text = Get-Content -LiteralPath $_.FullName -Raw
    $lines = $text -split "`r?`n"
    $matches = [regex]::Matches($text, $pattern)
    $real = 0
    foreach ($m in $matches) {
        # 注释里的示例（如 /// 说明不要写 catch { }）不是真实 catch，别计入棘轮。
        $lineNo = ($text.Substring(0, $m.Index) -split "`n").Count
        $line = if ($lineNo -le $lines.Count) { $lines[$lineNo - 1] } else { "" }
        if ($line.TrimStart().StartsWith('//')) { continue }
        $real++
    }
    if ($real -gt 0) {
        $relative = $_.FullName.Substring($Root.Length).TrimStart('\', '/')
        $area = ($relative -split '[\\/]')[1]
        if (-not $counts.ContainsKey($area)) { $counts[$area] = 0 }
        $counts[$area] += $real
        $total += $real
    }
}

Write-Host "[INFO] 空 catch 统计（$total 处，基线 $baseline）"
$counts.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object {
    Write-Host ("       {0,-12} {1}" -f $_.Key, $_.Value)
}

if ($total -gt $baseline) {
    Write-Host "[ERROR] 空 catch 数量比基线多了 $($total - $baseline) 处。" -ForegroundColor Red
    Write-Host "        新代码请用 SafeRelease.Run(...) 或带 Log.Warn/Error 的 catch；"
    Write-Host "        确实合法（Dispose 收尾）的，请放到 SafeRelease 调用里，不要新增裸 catch。"
    exit 1
}

if ($total -lt $baseline) {
    Write-Host "[INFO] 空 catch 已少于基线，请把 tools/empty-catch-baseline.txt 更新为 $total。" -ForegroundColor Yellow
}

Write-Host "[OK] 空 catch 棘轮通过。"
exit 0
