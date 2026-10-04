# NoteManager

轻量、本地优先的 Windows 桌面笔记应用。  
适合存放命令片段、密钥备忘、项目笔记等日常内容，数据完全保存在本机，不依赖云服务与账号登录。

## 界面概览

采用经典三栏布局：

- **左侧**：全部笔记 / 收藏 / 最近 / 回收站，以及文件夹管理、本地存储路径设置  
- **中间**：搜索、新建笔记、笔记列表（标题 + 摘要 + 时间）  
- **右侧**：标题与正文编辑、格式工具栏、收藏 / 置顶 / 删除、移动到文件夹

## 主要功能

- 笔记新建、编辑、删除，支持自动保存  
- 富文本编辑：加粗、斜体、下划线、字号、颜色、标题、列表等  
- 文件夹分类（根级文件夹，含默认文件夹），笔记可移动到指定文件夹  
- 收藏、置顶、回收站（软删除 / 恢复 / 永久删除）  
- 标题与正文全文搜索（SQLite FTS5，失败时回退 LIKE）  
- 可自定义笔记存储目录，并一键打开目录  
- 本地配置、日志与备份能力

## 技术栈

| 类别 | 技术 |
|------|------|
| 语言 / 运行时 | C# / .NET 8 |
| UI | WinUI 3 |
| 架构 | MVVM（CommunityToolkit.Mvvm） |
| 存储 | SQLite + EF Core + FTS5 |
| 日志 | Serilog |

## 环境要求

- Windows 10 1809（10.0.17763）及以上  
- 开发需安装 .NET 8 SDK  
- 项目已配置自包含（Self-Contained）发布，运行时可减少对系统全局 Runtime 的依赖

## 快速开始

### 编译

```powershell
dotnet restore NoteManager.sln
dotnet build NoteManager\NoteManager.csproj -c Debug


<img width="1326" height="718" alt="image" src="https://github.com/user-attachments/assets/16e166c2-b8bc-4043-8749-2377b1616bbe" />

