using System.Runtime.CompilerServices;

// 目的：讓另一個測試組件可直接注入固定識別碼，穩定測試碰撞，無須 Reflection 或公開建構子。

// 僅供 UnitTests 存取 internal test seam，不將測試入口變成 production API。
[assembly: InternalsVisibleTo("PhotoPlatform.UnitTests")]
