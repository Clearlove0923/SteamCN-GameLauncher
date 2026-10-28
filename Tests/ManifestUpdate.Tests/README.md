# 一键更新与 Manifest 备份回归检查

```powershell
dotnet run --project Tests/ManifestUpdate.Tests/ManifestUpdate.Tests.csproj
```

检查自动 Depot／EXE 决策、默认安装目录备份路径、覆盖前备份、内容相同时跳过写入，以及每个 AppID 最多两份且最长 60 天（约 2 个月）的滚动清理规则。每次成功更新（包括内容未变化）都会清理当前 AppID 的过期备份，测试覆盖 31 天保留、恰好 60 天保留和超过 60 天删除。实际写入检查使用随机临时目录，运行结束后删除。
