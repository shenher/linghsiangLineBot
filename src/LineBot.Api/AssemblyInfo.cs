using System.Runtime.CompilerServices;

// 讓測試專案可以直接測到 internal 類別（例如 OllamaChatClient），
// 不用為了測試把原本不該公開的實作細節改成 public。
[assembly: InternalsVisibleTo("LineBot.Api.Tests")]
