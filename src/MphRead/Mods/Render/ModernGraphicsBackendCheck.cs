using System;

namespace MphRead.Mods.Render
{
    public static class ModernGraphicsBackendCheck
    {
        public static int Run()
        {
            int failures = 0;
            CheckDefault(GraphicsPlatform.Windows, GraphicsBackend.DirectX12, ref failures);
            CheckDefault(GraphicsPlatform.MacOS, GraphicsBackend.Metal, ref failures);
            CheckDefault(GraphicsPlatform.Linux, GraphicsBackend.Vulkan, ref failures);
            CheckDefault(GraphicsPlatform.Android, GraphicsBackend.Vulkan, ref failures);

            CheckSupported(GraphicsPlatform.Windows, GraphicsBackend.DirectX12, true, ref failures);
            CheckSupported(GraphicsPlatform.Windows, GraphicsBackend.Vulkan, true, ref failures);
            CheckSupported(GraphicsPlatform.Windows, GraphicsBackend.Metal, false, ref failures);

            CheckSupported(GraphicsPlatform.MacOS, GraphicsBackend.Metal, true, ref failures);
            CheckSupported(GraphicsPlatform.MacOS, GraphicsBackend.Vulkan, true, ref failures);
            CheckSupported(GraphicsPlatform.MacOS, GraphicsBackend.DirectX12, false, ref failures);

            CheckSupported(GraphicsPlatform.Linux, GraphicsBackend.Vulkan, true, ref failures);
            CheckSupported(GraphicsPlatform.Linux, GraphicsBackend.Metal, false, ref failures);
            CheckSupported(GraphicsPlatform.Linux, GraphicsBackend.DirectX12, false, ref failures);

            CheckSupported(GraphicsPlatform.Android, GraphicsBackend.Vulkan, true, ref failures);
            CheckSupported(GraphicsPlatform.Android, GraphicsBackend.Metal, false, ref failures);
            CheckSupported(GraphicsPlatform.Android, GraphicsBackend.DirectX12, false, ref failures);

            CheckAlias("d3d12", GraphicsBackend.DirectX12, ref failures);
            CheckAlias("directx12", GraphicsBackend.DirectX12, ref failures);
            CheckAlias("vk", GraphicsBackend.Vulkan, ref failures);
            CheckAlias("moltenvk", GraphicsBackend.Vulkan, ref failures);
            CheckAlias("metal", GraphicsBackend.Metal, ref failures);
            CheckAlias("opengl", GraphicsBackend.OpenGL, ref failures);

            Console.WriteLine(failures == 0
                ? "RENDERBACKENDS all policy cases pass"
                : $"RENDERBACKENDS {failures} case(s) FAILED");
            return failures;
        }

        private static void CheckDefault(GraphicsPlatform platform, GraphicsBackend expected, ref int failures)
        {
            GraphicsBackend actual = GraphicsBackendPolicy.DefaultFor(platform);
            Check(actual == expected, $"{platform} default = {expected} (actual {actual})", ref failures);
        }

        private static void CheckSupported(GraphicsPlatform platform, GraphicsBackend backend,
            bool expected, ref int failures)
        {
            bool actual = GraphicsBackendPolicy.IsSupported(platform, backend);
            Check(actual == expected, $"{platform} {(expected ? "supports" : "rejects")} {backend}", ref failures);
        }

        private static void CheckAlias(string value, GraphicsBackend expected, ref int failures)
        {
            bool parsed = GraphicsBackendPolicy.TryParse(value, out GraphicsBackend actual);
            Check(parsed && actual == expected, $"'{value}' parses as {expected}", ref failures);
        }

        private static void Check(bool success, string name, ref int failures)
        {
            Console.WriteLine($"RENDERBACKENDS {(success ? "PASS" : "FAIL")} {name}");
            if (!success) failures++;
        }
    }
}
