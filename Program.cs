using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;
using Silk.NET.Input.Glfw;
using ImGuiNET;
using System.Numerics;

namespace Shark
{
    class Program
    {
        public static string gshCompilePath = "";

        static IWindow window = null!;
        static GL gl = null!;
        static ImGuiController imGuiController = null!;
        static IInputContext inputContext = null!;

        static SharcArchive? archive = null;
        static int selectedType = -1; // 0 = Program, 1 = Source
        static int selectedIndex = -1;
        
        static string currentFilePath = "";

        static bool shouldExit = false;
        static bool showConfirmModal = false;
        static bool showDuplicateModal = false;
        static bool showCompilerErrorModal = false;
        static bool showPreferencesModal = false;
        static string compilerErrorMessage = "";

        enum ConfirmAction { None, New, Open, Close, Exit }
        static ConfirmAction pendingAction = ConfirmAction.None;

        static void Main()
        {
            try {
                if (File.Exists("config.txt")) gshCompilePath = File.ReadAllText("config.txt").Trim();
            } catch {}

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            GlfwWindowing.RegisterPlatform();
            GlfwInput.RegisterPlatform();
            
            var options = WindowOptions.Default;
            options.Size = new Silk.NET.Maths.Vector2D<int>(1440, 900);
            options.Title = "Shark";
            options.VSync = false;
            options.FramesPerSecond = 120;
            options.UpdatesPerSecond = 120;
            window = Window.Create(options);

            window.Load += OnLoad;
            window.Render += OnRender;
            window.FramebufferResize += OnFramebufferResize;
            window.FileDrop += OnFileDrop;
            window.Closing += OnClose;

            window.Run();
        }

        private static void OnLoad()
        {
            gl = window.CreateOpenGL();
            inputContext = window.CreateInput();

            imGuiController = new ImGuiController(gl, window, inputContext, onConfigureIO: () => 
            {
                var io = ImGui.GetIO();
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("Shark.SpaceMono-Regular.ttf");
                
                if (stream != null)
                {
                    int fontDataLength = (int)stream.Length;
                    
                    unsafe
                    {
                        byte* nativePtr = (byte*)NativeMemory.Alloc((nuint)fontDataLength);
                        
                        using (UnmanagedMemoryStream writeStream = new UnmanagedMemoryStream(nativePtr, fontDataLength, fontDataLength, FileAccess.Write))
                        {
                            stream.CopyTo(writeStream);
                        }
                
                        float oversampleScale = 2.0f;
                        float baseFontSize = 16.0f;
                        
                        var font = io.Fonts.AddFontFromMemoryTTF((IntPtr)nativePtr, fontDataLength, baseFontSize * oversampleScale);
                        font.Scale = 1.0f / oversampleScale;
                    }
                }
                else
                {
                    Console.WriteLine("Warning: font not found. Falling back to default font.");
                }
            });

            ApplyProTheme();
        }

        private static void OnFileDrop(string[] files)
        {
            if (files.Length > 0 && !string.IsNullOrEmpty(files[0]))
            {
                try { LoadFile(files[0]); }
                catch (Exception ex) { Console.WriteLine(ex); }
            }
        }
        
        private static bool HasDuplicateNames()
        {
            if (archive == null) return false;
            
            bool hasDupPrograms = archive.Programs.Items.GroupBy(p => p.Name).Any(g => g.Count() > 1);
            bool hasDupCodes = archive.Codes.Items.GroupBy(c => c.Name).Any(g => g.Count() > 1);
            
            return hasDupPrograms || hasDupCodes;
        }

        private static void ApplyProTheme()
        {
            ImGui.StyleColorsDark();
            var style = ImGui.GetStyle();
            var colors = style.Colors;

            style.WindowPadding = new Vector2(10, 10);
            style.FramePadding = new Vector2(8, 6);
            style.ItemSpacing = new Vector2(8, 8);
            style.ItemInnerSpacing = new Vector2(6, 4);
            style.ScrollbarSize = 14.0f;
            style.IndentSpacing = 20.0f;

            style.WindowRounding = 0.0f;
            style.ChildRounding = 4.0f;
            style.FrameRounding = 3.0f;
            style.PopupRounding = 4.0f;
            style.ScrollbarRounding = 8.0f;
            style.GrabRounding = 3.0f;
            style.TabRounding = 4.0f;

            colors[(int)ImGuiCol.Text] = new Vector4(0.90f, 0.90f, 0.90f, 1.00f);
            colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.50f, 0.50f, 0.50f, 1.00f);
            colors[(int)ImGuiCol.WindowBg] = new Vector4(0.12f, 0.12f, 0.12f, 1.00f);
            colors[(int)ImGuiCol.ChildBg] = new Vector4(0.15f, 0.15f, 0.15f, 1.00f);
            colors[(int)ImGuiCol.PopupBg] = new Vector4(0.15f, 0.15f, 0.15f, 0.96f);
            colors[(int)ImGuiCol.Border] = new Vector4(0.24f, 0.24f, 0.24f, 1.00f);
            colors[(int)ImGuiCol.BorderShadow] = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);
            colors[(int)ImGuiCol.FrameBg] = new Vector4(0.20f, 0.20f, 0.20f, 1.00f);
            colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.27f, 0.27f, 0.27f, 1.00f);
            colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.35f, 0.35f, 0.35f, 1.00f);
            colors[(int)ImGuiCol.TitleBg] = new Vector4(0.12f, 0.12f, 0.12f, 1.00f);
            colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.12f, 0.12f, 0.12f, 1.00f);
            colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.00f, 0.00f, 0.00f, 0.51f);
            colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.12f, 0.12f, 0.12f, 1.00f);
            colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.15f, 0.15f, 0.15f, 1.00f);
            colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.31f, 0.31f, 0.31f, 1.00f);
            colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.41f, 0.41f, 0.41f, 1.00f);
            colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.51f, 0.51f, 0.51f, 1.00f);
            colors[(int)ImGuiCol.CheckMark] = new Vector4(0.26f, 0.59f, 0.98f, 1.00f);
            colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.24f, 0.52f, 0.88f, 1.00f);
            colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.26f, 0.59f, 0.98f, 1.00f);
            colors[(int)ImGuiCol.Button] = new Vector4(0.20f, 0.20f, 0.20f, 1.00f);
            colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.27f, 0.27f, 0.27f, 1.00f);
            colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.35f, 0.35f, 0.35f, 1.00f);
            colors[(int)ImGuiCol.Header] = new Vector4(0.18f, 0.35f, 0.58f, 1.00f);
            colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.26f, 0.59f, 0.98f, 0.80f);
            colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.26f, 0.59f, 0.98f, 1.00f);
            colors[(int)ImGuiCol.Separator] = new Vector4(0.24f, 0.24f, 0.24f, 1.00f);
            colors[(int)ImGuiCol.SeparatorHovered] = new Vector4(0.10f, 0.40f, 0.75f, 0.78f);
            colors[(int)ImGuiCol.SeparatorActive] = new Vector4(0.10f, 0.40f, 0.75f, 1.00f);
            colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.26f, 0.59f, 0.98f, 0.20f);
            colors[(int)ImGuiCol.ResizeGripHovered] = new Vector4(0.26f, 0.59f, 0.98f, 0.67f);
            colors[(int)ImGuiCol.ResizeGripActive] = new Vector4(0.26f, 0.59f, 0.98f, 0.95f);
            colors[(int)ImGuiCol.Tab] = new Vector4(0.15f, 0.15f, 0.15f, 1.00f);
            colors[(int)ImGuiCol.TabHovered] = new Vector4(0.26f, 0.59f, 0.98f, 0.80f);
            colors[(int)ImGuiCol.TabActive] = new Vector4(0.20f, 0.20f, 0.20f, 1.00f);
            colors[(int)ImGuiCol.TabUnfocused] = new Vector4(0.15f, 0.15f, 0.15f, 1.00f);
            colors[(int)ImGuiCol.TabUnfocusedActive] = new Vector4(0.20f, 0.20f, 0.20f, 1.00f);
            colors[(int)ImGuiCol.TableHeaderBg] = new Vector4(0.20f, 0.20f, 0.20f, 1.00f);
        }

        private static void OnRender(double delta)
        {
            if (inputContext != null && inputContext.Keyboards.Count > 0)
            {
                var io = ImGui.GetIO();
                var kb = inputContext.Keyboards[0];
                
                bool ctrl = kb.IsKeyPressed(Key.ControlLeft) || kb.IsKeyPressed(Key.ControlRight);
                bool shift = kb.IsKeyPressed(Key.ShiftLeft) || kb.IsKeyPressed(Key.ShiftRight);
                bool alt = kb.IsKeyPressed(Key.AltLeft) || kb.IsKeyPressed(Key.AltRight);
                bool super = kb.IsKeyPressed(Key.SuperLeft) || kb.IsKeyPressed(Key.SuperRight);
                
                io.KeyCtrl = ctrl;
                io.KeyShift = shift;
                io.KeyAlt = alt;
                io.KeySuper = super;
                
                io.AddKeyEvent(ImGuiKey.ModCtrl, ctrl);
                io.AddKeyEvent(ImGuiKey.ModShift, shift);
                io.AddKeyEvent(ImGuiKey.ModAlt, alt);
                io.AddKeyEvent(ImGuiKey.ModSuper, super);

                io.AddKeyEvent(ImGuiKey.Backspace, kb.IsKeyPressed(Key.Backspace));
                io.AddKeyEvent(ImGuiKey.Delete, kb.IsKeyPressed(Key.Delete));
                io.AddKeyEvent(ImGuiKey.LeftArrow, kb.IsKeyPressed(Key.Left));
                io.AddKeyEvent(ImGuiKey.RightArrow, kb.IsKeyPressed(Key.Right));
                io.AddKeyEvent(ImGuiKey.UpArrow, kb.IsKeyPressed(Key.Up));
                io.AddKeyEvent(ImGuiKey.DownArrow, kb.IsKeyPressed(Key.Down));
                io.AddKeyEvent(ImGuiKey.Enter, kb.IsKeyPressed(Key.Enter));
                io.AddKeyEvent(ImGuiKey.Escape, kb.IsKeyPressed(Key.Escape));
            }
            
            imGuiController.Update((float)delta);
            
            if (window.Size.X > 0 && window.Size.Y > 0)
            {
                var io = ImGui.GetIO();
                io.DisplayFramebufferScale = new Vector2(
                    (float)window.FramebufferSize.X / window.Size.X,
                    (float)window.FramebufferSize.Y / window.Size.Y
                );
            }

            gl.Viewport(window.FramebufferSize);
            gl.ClearColor(0.12f, 0.12f, 0.12f, 1.0f);
            gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

            RenderMainUI();

            imGuiController.Render();

            if (shouldExit)
            {
                window.Close();
            }
        }

        private static void OnFramebufferResize(Silk.NET.Maths.Vector2D<int> size)
        {
            gl.Viewport(size);
        }

        private static void OnClose()
        {
            if (archive != null && !shouldExit)
            {
                window.IsClosing = false; // consume event
                pendingAction = ConfirmAction.Exit;
                showConfirmModal = true;
                return;
            }

            imGuiController?.Dispose();
            inputContext?.Dispose();
            gl?.Dispose();
        }

        private static void RenderMainUI()
        {
            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(viewport.Pos);
            ImGui.SetNextWindowSize(viewport.Size);
            
            ImGui.Begin("Main Workspace", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.MenuBar | ImGuiWindowFlags.NoBringToFrontOnFocus);

            if (ImGui.BeginMenuBar())
            {
                if (ImGui.BeginMenu("File"))
                {
                    if (ImGui.MenuItem("New Archive")) 
                    { 
                        if (archive != null) { pendingAction = ConfirmAction.New; showConfirmModal = true; }
                        else CreateNewArchive();
                    }
                    
                    if (ImGui.MenuItem("Open...")) 
                    { 
                        if (archive != null) { pendingAction = ConfirmAction.Open; showConfirmModal = true; }
                        else ExecuteOpen();
                    }
                    
                    if (ImGui.MenuItem("Save", archive != null)) 
                    {
                        if (HasDuplicateNames()) showDuplicateModal = true;
                        else PerformSave();
                    }
                    
                    if (ImGui.MenuItem("Save As...", archive != null)) 
                    { 
                        if (HasDuplicateNames()) showDuplicateModal = true;
                        else
                        {
                            string? path = NativeFileDialog.ShowSaveFileDialog();
                            if (!string.IsNullOrEmpty(path))
                            {
                                archive.Header.Name = Path.GetFileNameWithoutExtension(path);
                                try { SaveFile(path); } catch (Exception ex) { Console.WriteLine(ex); }
                            }
                        }
                    }
                    
                    ImGui.Separator();

                    if (ImGui.MenuItem("Close Archive", archive != null))
                    {
                        pendingAction = ConfirmAction.Close; showConfirmModal = true;
                    }

                    if (ImGui.MenuItem("Exit")) 
                    { 
                        if (archive != null) { pendingAction = ConfirmAction.Exit; showConfirmModal = true; }
                        else shouldExit = true;
                    }

                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("Edit"))
                {
                    if (ImGui.MenuItem("Preferences")) showPreferencesModal = true;
                    ImGui.EndMenu();
                }

                ImGui.EndMenuBar();
            }

            RenderModals();

            if (archive != null)
            {
                if (ImGui.BeginTable("MainWorkspaceSplitter", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
                {
                    ImGui.TableSetupColumn("Hierarchy", ImGuiTableColumnFlags.WidthFixed, 300f);
                    ImGui.TableSetupColumn("Inspector", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableNextRow();
                    
                    ImGui.TableNextColumn();
                    DrawLeftPanel();
                    
                    ImGui.TableNextColumn();
                    DrawRightPanel();
                    
                    ImGui.EndTable();
                }
            }
            else
            {
                var avail = ImGui.GetContentRegionAvail();
                ImGui.SetCursorPos(new Vector2(avail.X * 0.5f - 150, avail.Y * 0.5f));
                ImGui.TextDisabled("Go to File -> Open or Drag & Drop a .sharc file here");
            }

            ImGui.End();
        }

        private static void RenderModals()
        {
            if (showConfirmModal) { ImGui.OpenPopup("Confirm Action"); showConfirmModal = false; }
            if (showDuplicateModal) { ImGui.OpenPopup("Save Blocked"); showDuplicateModal = false; }
            if (showCompilerErrorModal) { ImGui.OpenPopup("Compiler Error"); showCompilerErrorModal = false; }
            if (showPreferencesModal) { ImGui.OpenPopup("Preferences"); showPreferencesModal = false; }
            
            bool confirmModalOpen = true;
            if (ImGui.BeginPopupModal("Confirm Action", ref confirmModalOpen, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
            {
                ImGui.Text("Do you want to save your changes?");
                ImGui.Spacing(); ImGui.Spacing();
                
                if (ImGui.Button("Yes (Save)", new Vector2(130, 0)))
                {
                    if (HasDuplicateNames())
                    {
                        showDuplicateModal = true;
                        ImGui.CloseCurrentPopup();
                    }
                    else if (PerformSave()) 
                    {
                        ExecutePendingAction();
                        ImGui.CloseCurrentPopup();
                    }
                }
                ImGui.SameLine();
                
                if (ImGui.Button("No (Discard)", new Vector2(130, 0)))
                {
                    ExecutePendingAction();
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                
                if (ImGui.Button("Cancel", new Vector2(100, 0)))
                {
                    pendingAction = ConfirmAction.None;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }

            bool dupModalOpen = true;
            if (ImGui.BeginPopupModal("Save Blocked", ref dupModalOpen, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
            {
                ImGui.TextColored(new Vector4(0.7f, 0.2f, 0.2f, 1.0f), "ERROR: Duplicate Names Detected!");
                ImGui.Text("Multiple programs or sources share the same name.");
                ImGui.Text("This will cause file corruption on save. Please rename the conflicting items.");
                ImGui.Spacing(); ImGui.Spacing();
                
                if (ImGui.Button("OK", new Vector2(120, 0)))
                {
                    pendingAction = ConfirmAction.None;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }

            bool compilerErrorModalOpen = true;
            if (ImGui.BeginPopupModal("Compiler Error", ref compilerErrorModalOpen, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
            {
                ImGui.TextColored(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), "Compilation Error");
                ImGui.Separator();
                ImGui.Text(compilerErrorMessage);
                ImGui.Spacing(); ImGui.Spacing();
                
                if (ImGui.Button("Close", new Vector2(100, 0)))
                {
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }

            bool prefModalOpen = true;
            if (ImGui.BeginPopupModal("Preferences", ref prefModalOpen, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
            {
                ImGui.Text("Compiler Path (gshCompile.exe):");
                ImGui.SetNextItemWidth(400);
                ImGui.InputText("##gshpath", ref gshCompilePath, 1024);
                ImGui.SameLine();
                if (ImGui.Button("Browse..."))
                {
                    string? newPath = NativeFileDialog.ShowOpenFileDialog("Executables (*.exe)|*.exe|All Files (*.*)|*.*");
                    if (!string.IsNullOrEmpty(newPath)) 
                    {
                        gshCompilePath = newPath;
                        try { File.WriteAllText("config.txt", gshCompilePath); } catch {}
                    }
                }
                ImGui.Spacing(); ImGui.Spacing();
                if (ImGui.Button("Close", new Vector2(100, 0)))
                {
                    try { File.WriteAllText("config.txt", gshCompilePath); } catch {}
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
        }

        private static void ExecutePendingAction()
        {
            if (pendingAction == ConfirmAction.New) CreateNewArchive();
            else if (pendingAction == ConfirmAction.Open) ExecuteOpen();
            else if (pendingAction == ConfirmAction.Close) CloseArchive();
            else if (pendingAction == ConfirmAction.Exit) shouldExit = true;
            
            pendingAction = ConfirmAction.None;
        }

        private static void CreateNewArchive()
        {
            archive = new SharcArchive();
            currentFilePath = "";
            selectedType = -1;
            selectedIndex = -1;
        }

        private static void CloseArchive()
        {
            archive = null;
            currentFilePath = "";
            selectedType = -1;
            selectedIndex = -1;
        }

        private static void ExecuteOpen()
        {
            string? path = NativeFileDialog.ShowOpenFileDialog();
            if (!string.IsNullOrEmpty(path))
            {
                try { LoadFile(path); } catch (Exception ex) { Console.WriteLine(ex); }
            }
        }

        private static bool PerformSave()
        {
            if (string.IsNullOrEmpty(currentFilePath))
            {
                string? path = NativeFileDialog.ShowSaveFileDialog();
                if (!string.IsNullOrEmpty(path))
                {
                    archive!.Header.Name = Path.GetFileNameWithoutExtension(path);
                    try { SaveFile(path); return true; } catch (Exception ex) { Console.WriteLine(ex); return false; }
                }
                return false;
            }
            else
            {
                try { SaveFile(currentFilePath); return true; } catch (Exception ex) { Console.WriteLine(ex); return false; }
            }
        }

        private static void DrawLeftPanel()
        {
            if (archive == null) return;

            ImGui.BeginChild("LeftPaneBg", new Vector2(0, 0), true);

            var style = ImGui.GetStyle();
            float windowVisibleX2 = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
            float nextX;

            if (ImGui.Button("+ Program")) archive.Programs.Items.Add(new ShaderProgram { Name = "NewProgram" });
            
            nextX = ImGui.GetItemRectMax().X + style.ItemSpacing.X + ImGui.CalcTextSize("+ Source").X + style.FramePadding.X * 2.0f;
            if (nextX < windowVisibleX2) ImGui.SameLine();
            if (ImGui.Button("+ Source")) archive.Codes.Items.Add(new ShaderSource { Name = "new_source.glsl", Code = "// Write GLSL here\n" });
            
            bool hasSelection = selectedIndex >= 0;
            if (hasSelection)
            {
                nextX = ImGui.GetItemRectMax().X + style.ItemSpacing.X + ImGui.CalcTextSize("Duplicate").X + style.FramePadding.X * 2.0f;
                if (nextX < windowVisibleX2) ImGui.SameLine();
                if (ImGui.Button("Duplicate"))
                {
                    if (selectedType == 0 && selectedIndex < archive.Programs.Items.Count)
                    {
                        var copy = new ShaderProgram();
                        copy.Load(archive.Programs.Items[selectedIndex].Save(), 0);
                        copy.Name += "_copy";
                        archive.Programs.Items.Insert(selectedIndex + 1, copy);
                        selectedIndex++;
                    }
                    else if (selectedType == 1 && selectedIndex < archive.Codes.Items.Count)
                    {
                        var copy = new ShaderSource();
                        copy.Load(archive.Codes.Items[selectedIndex].Save(), 0);
                        copy.Name += "_copy";
                        archive.Codes.Items.Insert(selectedIndex + 1, copy);
                        selectedIndex++;
                    }
                }
                
                nextX = ImGui.GetItemRectMax().X + style.ItemSpacing.X + ImGui.CalcTextSize("Delete").X + style.FramePadding.X * 2.0f;
                if (nextX < windowVisibleX2) ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.3f, 0.3f, 1.0f));
                if (ImGui.Button("Delete"))
                {
                    if (selectedType == 0) archive.Programs.Items.RemoveAt(selectedIndex);
                    else if (selectedType == 1) archive.Codes.Items.RemoveAt(selectedIndex);
                    selectedIndex = -1;
                }
                ImGui.PopStyleColor(2);
            }

            nextX = ImGui.GetItemRectMax().X + style.ItemSpacing.X + ImGui.CalcTextSize("Compile...").X + style.FramePadding.X * 2.0f;
            if (nextX < windowVisibleX2) ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.18f, 0.35f, 0.58f, 1.00f));
            if (ImGui.Button("Compile..."))
            {
                if (HasDuplicateNames()) showDuplicateModal = true;
                else if (!File.Exists(gshCompilePath)) 
                {
                    compilerErrorMessage = $"gshCompile.exe not found at:\n{gshCompilePath}\n\nPlease update the gshCompilePath via Edit -> Preferences.";
                    showCompilerErrorModal = true;
                }
                else 
                {
                    string? path = NativeFileDialog.ShowSaveFileDialog("SharcFB Files (*.sharcfb)|*.sharcfb|All Files (*.*)|*.*");
                    if (!string.IsNullOrEmpty(path)) 
                    {
                        try { SharcCompiler.CompileAndSave(archive, path); } 
                        catch (Exception ex) { 
                            compilerErrorMessage = "Compilation Failed:\n" + ex.Message; 
                            showCompilerErrorModal = true; 
                        }
                    }
                }
            }
            ImGui.PopStyleColor();
            
            ImGui.Separator();
            ImGui.Spacing();

            var treeFlags = ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.OpenOnArrow;

            // Extract duplicate names for quick lookup
            var dupPrograms = archive.Programs.Items.GroupBy(p => p.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
            var dupCodes = archive.Codes.Items.GroupBy(c => c.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

            if (ImGui.TreeNodeEx("Shader Programs", treeFlags))
            {
                for (int i = 0; i < archive.Programs.Items.Count; i++)
                {
                    bool isSelected = (selectedType == 0 && selectedIndex == i);
                    bool isDuplicate = dupPrograms.Contains(archive.Programs.Items[i].Name);

                    if (isDuplicate) 
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.2f, 0.2f, 1.0f));

                    if (ImGui.Selectable($"{archive.Programs.Items[i].Name}##p{i}", isSelected)) {
                        selectedType = 0; selectedIndex = i;
                    }

                    if (isDuplicate) 
                    {
                        if (ImGui.IsItemHovered()) 
                            ImGui.SetTooltip("Duplicate name detected! This will cause corruption on save.");
                        ImGui.PopStyleColor();
                    }
                }
                ImGui.TreePop();
            }

            ImGui.Spacing();

            if (ImGui.TreeNodeEx("Shader Sources", treeFlags))
            {
                for (int i = 0; i < archive.Codes.Items.Count; i++)
                {
                    bool isSelected = (selectedType == 1 && selectedIndex == i);
                    bool isDuplicate = dupCodes.Contains(archive.Codes.Items[i].Name);

                    if (isDuplicate) 
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.2f, 0.2f, 1.0f));

                    if (ImGui.Selectable($"{archive.Codes.Items[i].Name}##c{i}", isSelected)) {
                        selectedType = 1; selectedIndex = i;
                    }

                    if (isDuplicate) 
                    {
                        if (ImGui.IsItemHovered()) 
                            ImGui.SetTooltip("Duplicate name detected! This will cause corruption on save.");
                        ImGui.PopStyleColor();
                    }
                }
                ImGui.TreePop();
            }

            ImGui.EndChild();
        }

        private static void DrawRightPanel()
        {
            if (archive == null) return;

            ImGui.BeginChild("RightPaneBg", new Vector2(0, 0), true);

            if (selectedType == 0 && selectedIndex >= 0 && selectedIndex < archive.Programs.Items.Count)
            {
                var prog = archive.Programs.Items[selectedIndex];
                
                ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "PROGRAM EDITOR");
                ImGui.Separator();
                ImGui.Spacing();

                string name = prog.Name;
                ImGui.AlignTextToFramePadding();
                ImGui.Text("Name:");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(300);
                if (ImGui.InputText("##ProgName", ref name, 256)) prog.Name = name;

                ImGui.Spacing(); ImGui.Spacing();
                
                if (ImGui.BeginTabBar("ProgramTabs"))
                {
                    if (ImGui.BeginTabItem(" Vertex ")) { DrawShaderStage(prog, 0); ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem(" Fragment ")) { DrawShaderStage(prog, 1); ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem(" Geometry ")) { DrawShaderStage(prog, 2); ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem(" Uniforms ")) {
                        DrawSymbolTable("Uniform Blocks", prog.UniformBlocks, true, true);
                        DrawSymbolTable("Uniform Variables", prog.UniformVariables, true, true);
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem(" Samplers ")) { DrawSymbolTable("Samplers", prog.SamplerVariables, false, false); ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem(" Attributes ")) { DrawSymbolTable("Vertex Attributes", prog.AttribVariables, false, false); ImGui.EndTabItem(); }
                    ImGui.EndTabBar();
                }
            }
            else if (selectedType == 1 && selectedIndex >= 0 && selectedIndex < archive.Codes.Items.Count)
            {
                var source = archive.Codes.Items[selectedIndex];
                
                ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.6f, 1.0f), "SOURCE EDITOR");
                ImGui.Separator();
                ImGui.Spacing();

                string name = source.Name;
                ImGui.AlignTextToFramePadding();
                ImGui.Text("Filename:");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(400);
                if (ImGui.InputText("##SourceName", ref name, 256)) source.Name = name;

                ImGui.Spacing();
                
                ImGui.PushFont(ImGui.GetIO().Fonts.Fonts[0]); 
                string code = source.Code;
                GlslCodeEditor.Draw(ref code);
                source.Code = code;
                ImGui.PopFont();
            }
            else
            {
                var avail = ImGui.GetContentRegionAvail();
                ImGui.SetCursorPos(new Vector2(avail.X * 0.5f - 100, avail.Y * 0.5f));
                ImGui.TextDisabled("Select an item to view properties");
            }

            ImGui.EndChild();
        }

        static void DrawShaderStage(ShaderProgram prog, int stage)
        {
            if (archive == null) return;

            string[] sourceNames = archive.Codes.Items.Select(c => c.Name).ToArray();
            
            ImGui.Spacing(); ImGui.Spacing();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Linked Source:");
            ImGui.SameLine();
            
            if (stage == 0) {
                int idx = prog.VtxShIdx; DrawCombo("##VtxCombo", ref idx, sourceNames); prog.VtxShIdx = idx;
                ImGui.Spacing(); DrawMacroTable("Vertex Macros", prog.VertexMacros);
            }
            else if (stage == 1) {
                int idx = prog.FrgShIdx; DrawCombo("##FrgCombo", ref idx, sourceNames); prog.FrgShIdx = idx;
                ImGui.Spacing(); DrawMacroTable("Fragment Macros", prog.FragmentMacros);
            }
            else if (stage == 2) {
                int idx = prog.GeoShIdx; DrawCombo("##GeoCombo", ref idx, sourceNames); prog.GeoShIdx = idx;
                ImGui.Spacing(); DrawMacroTable("Geometry Macros", prog.GeometryMacros);
            }
        }

        static void DrawCombo(string label, ref int selectedIdx, string[] sources)
        {
            int comboIdx = selectedIdx + 1;
            string[] items = new string[sources.Length + 1];
            items[0] = "None";
            Array.Copy(sources, 0, items, 1, sources.Length);

            ImGui.SetNextItemWidth(350);
            if (ImGui.Combo(label, ref comboIdx, items, items.Length)) selectedIdx = comboIdx - 1;
        }

        static void DrawMacroTable(string title, SharcList<ShaderMacro> macros)
        {
            ImGui.Spacing();
            ImGui.Text(title);
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - 80);
            if (ImGui.Button($"+ Add Macro##{title}", new Vector2(90, 0))) macros.Items.Add(new ShaderMacro { Name = "NEW_MAC", Value = "1" });

            var tableFlags = ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable;
            if (ImGui.BeginTable($"##Table_{title}", 3, tableFlags))
            {
                ImGui.TableSetupColumn("Macro Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Act", ImGuiTableColumnFlags.WidthFixed, 35f);
                ImGui.TableHeadersRow();

                for (int i = 0; i < macros.Items.Count; i++)
                {
                    var m = macros.Items[i];
                    ImGui.TableNextRow();
                    
                    ImGui.TableNextColumn();
                    string name = m.Name;
                    ImGui.SetNextItemWidth(-1.0f);
                    if (ImGui.InputText($"##name{i}_{title}", ref name, 256)) m.Name = name;

                    ImGui.TableNextColumn();
                    string val = m.Value;
                    ImGui.SetNextItemWidth(-1.0f);
                    if (ImGui.InputText($"##val{i}_{title}", ref val, 256)) m.Value = val;

                    ImGui.TableNextColumn();
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.3f, 0.3f, 1.0f));
                    if (ImGui.Button($"X##{i}_{title}", new Vector2(35, 0))) { macros.Items.RemoveAt(i); i--; }
                    ImGui.PopStyleColor(2);
                }
                ImGui.EndTable();
            }
        }

        static void DrawSymbolTable(string title, SharcList<ShaderSymbol> list, bool showOffset, bool showDefault)
        {
            ImGui.Spacing(); ImGui.Spacing();
            ImGui.Text(title);
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - 80);
            if (ImGui.Button($"+ Add Item##{title}", new Vector2(80, 0))) list.Items.Add(new ShaderSymbol { Name = "NewSym", ID = "new_id" });
        
            int cols = 3 + (showOffset ? 1 : 0) + (showDefault ? 1 : 0);
            var tableFlags = ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable;
            
            if (ImGui.BeginTable($"##Table_{title}", cols, tableFlags))
            {
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthStretch);
                if (showOffset) ImGui.TableSetupColumn("Param", ImGuiTableColumnFlags.WidthFixed, 80f);
                if (showDefault) ImGui.TableSetupColumn("Default Val (Hex)", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Act", ImGuiTableColumnFlags.WidthFixed, 35f);
                ImGui.TableHeadersRow();
        
                for (int i = 0; i < list.Items.Count; i++)
                {
                    var sym = list.Items[i];
                    ImGui.TableNextRow();
        
                    ImGui.TableNextColumn();
                    string name = sym.Name; ImGui.SetNextItemWidth(-1.0f);
                    if (ImGui.InputText($"##name{i}_{title}", ref name, 256)) sym.Name = name;
        
                    ImGui.TableNextColumn();
                    string id = sym.ID; ImGui.SetNextItemWidth(-1.0f);
                    if (ImGui.InputText($"##id{i}_{title}", ref id, 256)) sym.ID = id;
        
                    if (showOffset) {
                        ImGui.TableNextColumn();
                        int param = sym.Param; ImGui.SetNextItemWidth(-1.0f);
                        if (ImGui.InputInt($"##param{i}_{title}", ref param, 0)) sym.Param = param;
                    }
        
                    if (showDefault) {
                        ImGui.TableNextColumn();
                        string hex = BitConverter.ToString(sym.DefaultValue).Replace("-", " ");
                        ImGui.SetNextItemWidth(-1.0f);
                        if (ImGui.InputText($"##defval{i}_{title}", ref hex, 512)) sym.DefaultValue = HexToBytes(hex);
                    }
        
                    ImGui.TableNextColumn();
                    
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.3f, 0.3f, 1.0f));
                    if (ImGui.Button($"X##{i}_{title}", new Vector2(35, 0))) { list.Items.RemoveAt(i); i--; }
                    ImGui.PopStyleColor(2);
                }
                ImGui.EndTable();
            }
        }

        static byte[] HexToBytes(string hex)
        {
            hex = hex.Replace(" ", "").Replace("-", "");
            if (hex.Length % 2 != 0) return Array.Empty<byte>();
            try {
                byte[] ret = new byte[hex.Length / 2];
                for (int i = 0; i < ret.Length; i++) ret[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                return ret;
            } catch { return Array.Empty<byte>(); }
        }

        static void LoadFile(string path)
        {
            byte[] fileData = File.ReadAllBytes(path);
            archive = new SharcArchive();
            archive.Load(fileData);
            currentFilePath = path;
            selectedType = -1; selectedIndex = -1;
        }

        static void SaveFile(string path)
        {
            if (archive == null) return;
            byte[] outData = archive.Save();
            File.WriteAllBytes(path, outData);
            currentFilePath = path;
        }
    }

    public static class StringUtils
    {
        public static string ReadZString(byte[] data, int pos, int len, Encoding? encoding = null)
        {
            encoding ??= Encoding.UTF8;
            string s = encoding.GetString(data, pos, len);
            int idx = s.IndexOf('\0');
            return idx >= 0 ? s.Substring(0, idx) : s;
        }
    }

    public static class GlslCodeEditor
    {
        public static void Draw(ref string code)
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.08f, 1.0f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
            ImGui.BeginChild("CodeEditorBg", ImGui.GetContentRegionAvail(), true, ImGuiWindowFlags.HorizontalScrollbar);

            Vector2 startScreenPos = ImGui.GetCursorScreenPos();
            Vector2 winPos = ImGui.GetWindowPos();
            float winHeight = ImGui.GetWindowHeight();

            string[] lines = code.Split('\n');
            int lineCount = lines.Length;
            float lineHeight = ImGui.GetTextLineHeight();
            
            float textOffsetX = ImGui.GetStyle().FramePadding.X;
            float textOffsetY = ImGui.GetStyle().FramePadding.Y;
            float leftPad = 50.0f;

            float scrollY = ImGui.GetScrollY();
            int startIdx = Math.Max(0, (int)(scrollY / lineHeight));
            int endIdx = Math.Min(lineCount, startIdx + (int)(winHeight / lineHeight) + 2);

            ImGui.SetCursorPos(new Vector2(leftPad, 0));

            ImGui.PushItemWidth(-1);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.Border, Vector4.Zero);
            
            ImGui.InputTextMultiline("##editor", ref code, 1024 * 1024, new Vector2(-1, lineCount * lineHeight + 20), ImGuiInputTextFlags.AllowTabInput);
            
            ImGui.PopStyleColor(2);
            ImGui.PopItemWidth();

            var drawList = ImGui.GetWindowDrawList();
            uint numColor = ImGui.GetColorU32(new Vector4(0.3f, 0.4f, 0.5f, 1.0f));
            uint dividerColor = ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.2f, 1.0f));
            uint bgGutterColor = ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.12f, 1.0f));

            drawList.AddRectFilled(new Vector2(winPos.X, winPos.Y), new Vector2(winPos.X + leftPad - 5, winPos.Y + winHeight), bgGutterColor);
            drawList.AddLine(new Vector2(winPos.X + leftPad - 5, winPos.Y), new Vector2(winPos.X + leftPad - 5, winPos.Y + winHeight), dividerColor);

            for (int i = startIdx; i < endIdx; i++)
            {
                string lineNum = (i + 1).ToString();
                float textWidth = ImGui.CalcTextSize(lineNum).X;
                float lineY = startScreenPos.Y + textOffsetY + (i * lineHeight);
                
                if (lineY + lineHeight > winPos.Y && lineY < winPos.Y + winHeight)
                {
                    drawList.AddText(new Vector2(winPos.X + leftPad - 10 - textWidth, lineY), numColor, lineNum);
                }
            }

            ImGui.EndChild();
            ImGui.PopStyleVar();
            ImGui.PopStyleColor();
        }
    }

    public static class NativeFileDialog
    {
        public static string? ShowOpenFileDialog(string filter = "Sharc Files (*.sharc)|*.sharc|All Files (*.*)|*.*")
        {
            if (OperatingSystem.IsWindows())
            {
                return RunProcess("powershell.exe", $"-Command \"Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.OpenFileDialog; $f.Filter = '{filter}'; $f.ShowHelp = $true; if($f.ShowDialog() -eq 'OK'){{ $f.FileName }}\"");
            }
            else if (OperatingSystem.IsLinux())
            {
                string? res = RunProcess("zenity", "--file-selection --title=\"Open File\"");
                if (string.IsNullOrEmpty(res)) res = RunProcess("kdialog", "--getopenfilename");
                return res;
            }
            else if (OperatingSystem.IsMacOS())
            {
                return RunProcess("osascript", "-e \"POSIX path of (choose file with prompt \\\"Select File\\\")\"");
            }
            return null;
        }

        public static string? ShowSaveFileDialog(string filter = "Sharc Files (*.sharc)|*.sharc|All Files (*.*)|*.*")
        {
            if (OperatingSystem.IsWindows())
            {
                return RunProcess("powershell.exe", $"-Command \"Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.SaveFileDialog; $f.Filter = '{filter}'; $f.ShowHelp = $true; if($f.ShowDialog() -eq 'OK'){{ $f.FileName }}\"");
            }
            else if (OperatingSystem.IsLinux())
            {
                string? res = RunProcess("zenity", "--file-selection --save --title=\"Save File\"");
                if (string.IsNullOrEmpty(res)) res = RunProcess("kdialog", "--getsavefilename");
                return res;
            }
            else if (OperatingSystem.IsMacOS())
            {
                return RunProcess("osascript", "-e \"POSIX path of (choose file name with prompt \\\"Save File As\\\")\"");
            }
            return null;
        }

        private static string? RunProcess(string fileName, string args)
        {
            try
            {
                var ps = new System.Diagnostics.Process();
                ps.StartInfo.FileName = fileName;
                ps.StartInfo.Arguments = args;
                ps.StartInfo.UseShellExecute = false;
                ps.StartInfo.RedirectStandardOutput = true;
                ps.StartInfo.CreateNoWindow = true;
                ps.Start();
                string output = ps.StandardOutput.ReadToEnd().Trim();
                ps.WaitForExit();
                return string.IsNullOrEmpty(output) ? null : output;
            }
            catch
            {
                return null;
            }
        }
    }

    public abstract class SharcNode
    {
        public uint Size { get; set; }
        public abstract void Load(byte[] data, int pos);
        public abstract byte[] Save();
    }

    public class SharcArchive
    {
        public SharcHeader Header { get; set; } = new SharcHeader();
        public SharcList<ShaderProgram> Programs { get; set; } = new SharcList<ShaderProgram>();
        public SharcList<ShaderSource> Codes { get; set; } = new SharcList<ShaderSource>();

        public void Load(byte[] data)
        {
            Header.Load(data, 0);
            int pos = (int)Header.Size;
            Programs.Load(data, pos);
            pos += (int)Programs.Size;
            Codes.Load(data, pos);
        }

        public byte[] Save()
        {
            byte[] headerBytes = Header.Save();
            byte[] progsBytes = Programs.Save();
            byte[] codesBytes = Codes.Save();

            uint totalSize = (uint)(headerBytes.Length + progsBytes.Length + codesBytes.Length);
            byte[] sizeBytes = BitConverter.GetBytes(totalSize);
            Array.Copy(sizeBytes, 0, headerBytes, 8, 4);

            List<byte> final = new List<byte>((int)totalSize);
            final.AddRange(headerBytes);
            final.AddRange(progsBytes);
            final.AddRange(codesBytes);
            return final.ToArray();
        }
    }

    public class SharcList<T> : SharcNode where T : SharcNode, new()
    {
        public List<T> Items { get; set; } = new List<T>();

        public override void Load(byte[] data, int pos)
        {
            Size = BitConverter.ToUInt32(data, pos);
            uint count = BitConverter.ToUInt32(data, pos + 4);
            int p = pos + 8;

            Items.Clear();
            for (int i = 0; i < count; i++) {
                T item = new T();
                item.Load(data, p);
                Items.Add(item);
                p += (int)item.Size;
            }
        }

        public override byte[] Save()
        {
            List<byte> itemsBuf = new List<byte>();
            foreach (var item in Items) itemsBuf.AddRange(item.Save());

            Size = (uint)(8 + itemsBuf.Count);
            List<byte> outBuf = new List<byte>((int)Size);
            outBuf.AddRange(BitConverter.GetBytes(Size));
            outBuf.AddRange(BitConverter.GetBytes((uint)Items.Count));
            outBuf.AddRange(itemsBuf);
            return outBuf.ToArray();
        }
    }

    public class SharcHeader : SharcNode
    {
        public uint Magic { get; set; } = 0x53484141; // SHAA
        public uint Version { get; set; } = 11;
        public uint FileSize { get; set; } = 0;
        public uint Endianness { get; set; } = 1;
        public string Name { get; set; } = "";

        public override void Load(byte[] data, int pos)
        {
            Magic = BitConverter.ToUInt32(data, pos);
            Version = BitConverter.ToUInt32(data, pos + 4);
            FileSize = BitConverter.ToUInt32(data, pos + 8);
            Endianness = BitConverter.ToUInt32(data, pos + 12);
            uint nameLen = BitConverter.ToUInt32(data, pos + 16);
            Name = StringUtils.ReadZString(data, pos + 20, (int)nameLen);
            Size = 20 + nameLen;
        }

        public override byte[] Save()
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(Name + "\0");
            Size = (uint)(20 + nameBytes.Length);
            List<byte> outBuf = new List<byte>((int)Size);
            outBuf.AddRange(BitConverter.GetBytes(Magic));
            outBuf.AddRange(BitConverter.GetBytes(Version));
            outBuf.AddRange(BitConverter.GetBytes(FileSize)); 
            outBuf.AddRange(BitConverter.GetBytes(Endianness));
            outBuf.AddRange(BitConverter.GetBytes((uint)nameBytes.Length));
            outBuf.AddRange(nameBytes);
            return outBuf.ToArray();
        }
    }

    public class ShaderMacro : SharcNode
    {
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";

        public override void Load(byte[] data, int pos)
        {
            Size = BitConverter.ToUInt32(data, pos);
            uint nameLen = BitConverter.ToUInt32(data, pos + 4);
            uint valueLen = BitConverter.ToUInt32(data, pos + 8);
            int p = pos + 12;
            Name = StringUtils.ReadZString(data, p, (int)nameLen);
            p += (int)nameLen;
            Value = StringUtils.ReadZString(data, p, (int)valueLen);
        }

        public override byte[] Save()
        {
            byte[] nBytes = Encoding.UTF8.GetBytes(Name + "\0");
            byte[] vBytes = Encoding.UTF8.GetBytes(Value + "\0");
            Size = (uint)(12 + nBytes.Length + vBytes.Length);

            List<byte> outBuf = new List<byte>((int)Size);
            outBuf.AddRange(BitConverter.GetBytes(Size));
            outBuf.AddRange(BitConverter.GetBytes((uint)nBytes.Length));
            outBuf.AddRange(BitConverter.GetBytes((uint)vBytes.Length));
            outBuf.AddRange(nBytes);
            outBuf.AddRange(vBytes);
            return outBuf.ToArray();
        }
    }

    public class ShaderSymbol : SharcNode
    {
        public int Param { get; set; }
        public string Name { get; set; } = "";
        public string ID { get; set; } = "";
        public byte[] DefaultValue { get; set; } = Array.Empty<byte>();
        public bool[] ValidVariations { get; set; } = Array.Empty<bool>();

        public override void Load(byte[] data, int pos)
        {
            Size = BitConverter.ToUInt32(data, pos);
            Param = BitConverter.ToInt32(data, pos + 4);
            uint nameLen = BitConverter.ToUInt32(data, pos + 8);
            uint idLen = BitConverter.ToUInt32(data, pos + 12);
            uint defLen = BitConverter.ToUInt32(data, pos + 16);
            uint varCount = BitConverter.ToUInt32(data, pos + 20);

            int p = pos + 24;
            Name = StringUtils.ReadZString(data, p, (int)nameLen); p += (int)nameLen;
            ID = StringUtils.ReadZString(data, p, (int)idLen); p += (int)idLen;

            DefaultValue = new byte[defLen];
            Array.Copy(data, p, DefaultValue, 0, defLen); p += (int)defLen;

            ValidVariations = new bool[varCount];
            for (int i = 0; i < varCount; i++) ValidVariations[i] = data[p + i] != 0;
        }

        public override byte[] Save()
        {
            byte[] nBytes = Encoding.UTF8.GetBytes(Name + "\0");
            byte[] idBytes = Encoding.UTF8.GetBytes(ID + "\0");
            Size = (uint)(24 + nBytes.Length + idBytes.Length + DefaultValue.Length + ValidVariations.Length);

            List<byte> outBuf = new List<byte>((int)Size);
            outBuf.AddRange(BitConverter.GetBytes(Size));
            outBuf.AddRange(BitConverter.GetBytes(Param));
            outBuf.AddRange(BitConverter.GetBytes((uint)nBytes.Length));
            outBuf.AddRange(BitConverter.GetBytes((uint)idBytes.Length));
            outBuf.AddRange(BitConverter.GetBytes((uint)DefaultValue.Length));
            outBuf.AddRange(BitConverter.GetBytes((uint)ValidVariations.Length));
            outBuf.AddRange(nBytes);
            outBuf.AddRange(idBytes);
            outBuf.AddRange(DefaultValue);
            foreach (var b in ValidVariations) outBuf.Add(b ? (byte)1 : (byte)0);
            return outBuf.ToArray();
        }
    }

    public class ShaderVariation : SharcNode
    {
        public string Name { get; set; } = "";
        public string ID { get; set; } = "";
        public List<string> Values { get; set; } = new List<string>();

        public override void Load(byte[] data, int pos)
        {
            Size = BitConverter.ToUInt32(data, pos);
            uint nameLen = BitConverter.ToUInt32(data, pos + 4);
            uint vCount = BitConverter.ToUInt32(data, pos + 8);
            uint idLen = BitConverter.ToUInt32(data, pos + 12);

            int p = pos + 16;
            Name = StringUtils.ReadZString(data, p, (int)nameLen); p += (int)nameLen;

            Values.Clear();
            for (int i = 0; i < vCount; i++)
            {
                while (data[p] == 0) p++;
                int s_pos = p++;
                while (data[p] != 0) p++;
                p++;
                Values.Add(StringUtils.ReadZString(data, s_pos, p - s_pos));
            }
            ID = StringUtils.ReadZString(data, p, (int)idLen);
        }

        public override byte[] Save()
        {
            byte[] nBytes = Encoding.UTF8.GetBytes(Name + "\0");
            byte[] idBytes = Encoding.UTF8.GetBytes(ID + "\0");
            List<byte> valsBuf = new List<byte>();
            foreach (var v in Values) valsBuf.AddRange(Encoding.UTF8.GetBytes(v + "\0"));

            Size = (uint)(16 + nBytes.Length + valsBuf.Count + idBytes.Length);
            List<byte> outBuf = new List<byte>((int)Size);
            outBuf.AddRange(BitConverter.GetBytes(Size));
            outBuf.AddRange(BitConverter.GetBytes((uint)nBytes.Length));
            outBuf.AddRange(BitConverter.GetBytes((uint)Values.Count));
            outBuf.AddRange(BitConverter.GetBytes((uint)idBytes.Length));
            outBuf.AddRange(nBytes);
            outBuf.AddRange(valsBuf);
            outBuf.AddRange(idBytes);
            return outBuf.ToArray();
        }
    }

    public class ShaderSource : SharcNode
    {
        public string Name { get; set; } = "";
        public string Code { get; set; } = "";
        
        private uint _originalCodeLen;
        private uint _codeLen2;

        public override void Load(byte[] data, int pos)
        {
            Size = BitConverter.ToUInt32(data, pos);
            uint nameLen = BitConverter.ToUInt32(data, pos + 4);
            uint codeLen = BitConverter.ToUInt32(data, pos + 8);
            
            _originalCodeLen = codeLen;
            _codeLen2 = BitConverter.ToUInt32(data, pos + 12);

            int p = pos + 16;
            Name = StringUtils.ReadZString(data, p, (int)nameLen); p += (int)nameLen;
            Code = Encoding.GetEncoding("shift_jis").GetString(data, p, (int)codeLen);
        }

        public override byte[] Save()
        {
            byte[] nBytes = Encoding.UTF8.GetBytes(Name + "\0");
            byte[] cBytes = Encoding.GetEncoding("shift_jis").GetBytes(Code);
            uint cLen = (uint)cBytes.Length;

            uint finalCodeLen2 = (cLen == _originalCodeLen) ? _codeLen2 : cLen;

            Size = (uint)(16 + nBytes.Length + cBytes.Length);
            List<byte> outBuf = new List<byte>((int)Size);
            outBuf.AddRange(BitConverter.GetBytes(Size));
            outBuf.AddRange(BitConverter.GetBytes((uint)nBytes.Length));
            outBuf.AddRange(BitConverter.GetBytes(cLen));
            outBuf.AddRange(BitConverter.GetBytes(finalCodeLen2));
            outBuf.AddRange(nBytes);
            outBuf.AddRange(cBytes);
            return outBuf.ToArray();
        }
    }

    public class ShaderProgram : SharcNode
    {
        public string Name { get; set; } = "";
        public int VtxShIdx { get; set; } = -1;
        public int FrgShIdx { get; set; } = -1;
        public int GeoShIdx { get; set; } = -1;

        public SharcList<ShaderMacro> VertexMacros { get; set; } = new SharcList<ShaderMacro>();
        public SharcList<ShaderMacro> FragmentMacros { get; set; } = new SharcList<ShaderMacro>();
        public SharcList<ShaderMacro> GeometryMacros { get; set; } = new SharcList<ShaderMacro>();
        public SharcList<ShaderVariation> Variations { get; set; } = new SharcList<ShaderVariation>();
        public SharcList<ShaderVariation> VariationDefaults { get; set; } = new SharcList<ShaderVariation>();
        public SharcList<ShaderSymbol> UniformVariables { get; set; } = new SharcList<ShaderSymbol>();
        public SharcList<ShaderSymbol> UniformBlocks { get; set; } = new SharcList<ShaderSymbol>();
        public SharcList<ShaderSymbol> SamplerVariables { get; set; } = new SharcList<ShaderSymbol>();
        public SharcList<ShaderSymbol> AttribVariables { get; set; } = new SharcList<ShaderSymbol>();

        public override void Load(byte[] data, int pos)
        {
            Size = BitConverter.ToUInt32(data, pos);
            uint nameLen = BitConverter.ToUInt32(data, pos + 4);
            VtxShIdx = BitConverter.ToInt32(data, pos + 8);
            FrgShIdx = BitConverter.ToInt32(data, pos + 12);
            GeoShIdx = BitConverter.ToInt32(data, pos + 16);

            int p = pos + 20;
            Name = StringUtils.ReadZString(data, p, (int)nameLen); p += (int)nameLen;

            VertexMacros.Load(data, p); p += (int)VertexMacros.Size;
            FragmentMacros.Load(data, p); p += (int)FragmentMacros.Size;
            GeometryMacros.Load(data, p); p += (int)GeometryMacros.Size;
            Variations.Load(data, p); p += (int)Variations.Size;
            VariationDefaults.Load(data, p); p += (int)VariationDefaults.Size;
            UniformVariables.Load(data, p); p += (int)UniformVariables.Size;
            UniformBlocks.Load(data, p); p += (int)UniformBlocks.Size;
            SamplerVariables.Load(data, p); p += (int)SamplerVariables.Size;
            AttribVariables.Load(data, p); p += (int)AttribVariables.Size;
        }

        public override byte[] Save()
        {
            byte[] nBytes = Encoding.UTF8.GetBytes(Name + "\0");
            byte[] vmB = VertexMacros.Save(), fmB = FragmentMacros.Save(), gmB = GeometryMacros.Save();
            byte[] vB = Variations.Save(), vdB = VariationDefaults.Save();
            byte[] uvB = UniformVariables.Save(), ubB = UniformBlocks.Save(), svB = SamplerVariables.Save(), avB = AttribVariables.Save();

            Size = (uint)(20 + nBytes.Length + vmB.Length + fmB.Length + gmB.Length + vB.Length + vdB.Length + uvB.Length + ubB.Length + svB.Length + avB.Length);

            List<byte> outBuf = new List<byte>((int)Size);
            outBuf.AddRange(BitConverter.GetBytes(Size));
            outBuf.AddRange(BitConverter.GetBytes((uint)nBytes.Length));
            outBuf.AddRange(BitConverter.GetBytes(VtxShIdx));
            outBuf.AddRange(BitConverter.GetBytes(FrgShIdx));
            outBuf.AddRange(BitConverter.GetBytes(GeoShIdx));
            outBuf.AddRange(nBytes);
            outBuf.AddRange(vmB); outBuf.AddRange(fmB); outBuf.AddRange(gmB);
            outBuf.AddRange(vB); outBuf.AddRange(vdB);
            outBuf.AddRange(uvB); outBuf.AddRange(ubB); outBuf.AddRange(svB); outBuf.AddRange(avB);
            return outBuf.ToArray();
        }
    }

    public static class SharcCompiler
    {
        static uint ReadUInt32BE(byte[] data, int pos) {
            return (uint)(data[pos] << 24 | data[pos+1] << 16 | data[pos+2] << 8 | data[pos+3]);
        }

        static void WriteUInt32LE(List<byte> list, uint val) {
            list.Add((byte)(val & 0xFF));
            list.Add((byte)((val >> 8) & 0xFF));
            list.Add((byte)((val >> 16) & 0xFF));
            list.Add((byte)((val >> 24) & 0xFF));
        }

        static string ReadString(byte[] data, int offset) {
            int end = offset;
            while (end < data.Length && data[end] != 0) end++;
            return Encoding.UTF8.GetString(data, offset, end - offset);
        }

        // multiple identical entries grow the data payload but alias to the earliest instance
        class StringTable {
            public List<byte> Data = new List<byte>();
            public int Offset;
            class Entry { public string Value = ""; public int Pos; }
            List<Entry> Items = new List<Entry>();

            public StringTable(int offset) { Offset = offset; }
            
            public void Append(string val) {
                Items.Add(new Entry { Value = val, Pos = Data.Count });
                Data.AddRange(Encoding.UTF8.GetBytes(val));
                Data.Add(0);
            }
            public uint GetPos(string val) {
                foreach(var entry in Items) {
                    if (entry.Value == val) return (uint)(Offset + entry.Pos);
                }
                return 0;
            }
        }

        class GX2RBuffer {
            public uint resourceFlags, elementSize, elementCount;
            public void Load(byte[] data, int pos) {
                resourceFlags = ReadUInt32BE(data, pos);
                elementSize = ReadUInt32BE(data, pos + 4);
                elementCount = ReadUInt32BE(data, pos + 8);
            }
            public void SaveLE(List<byte> outBuf) {
                WriteUInt32LE(outBuf, resourceFlags);
                WriteUInt32LE(outBuf, elementSize);
                WriteUInt32LE(outBuf, elementCount);
                WriteUInt32LE(outBuf, 0); // 4x pad
            }
        }

        class GX2UniformBlock {
            public string name = "";
            public uint location, size;
            public void Load(byte[] data, int pos, int shaderPos) {
                uint nameOffs = ReadUInt32BE(data, pos);
                location = ReadUInt32BE(data, pos + 4);
                size = ReadUInt32BE(data, pos + 8);
                name = ReadString(data, shaderPos + (int)(nameOffs & ~0xCA700000));
            }
            public void SaveLE(List<byte> outBuf, uint nameOffs) {
                WriteUInt32LE(outBuf, nameOffs);
                WriteUInt32LE(outBuf, location);
                WriteUInt32LE(outBuf, size);
            }
        }

        class GX2UniformVar {
            public string name = "";
            public uint type, arrayCount, offset, blockIndex;
            public void Load(byte[] data, int pos, int shaderPos) {
                uint nameOffs = ReadUInt32BE(data, pos);
                type = ReadUInt32BE(data, pos + 4);
                arrayCount = ReadUInt32BE(data, pos + 8);
                offset = ReadUInt32BE(data, pos + 12);
                blockIndex = ReadUInt32BE(data, pos + 16);
                name = ReadString(data, shaderPos + (int)(nameOffs & ~0xCA700000));
            }
            public void SaveLE(List<byte> outBuf, uint nameOffs) {
                WriteUInt32LE(outBuf, nameOffs); WriteUInt32LE(outBuf, type); WriteUInt32LE(outBuf, arrayCount);
                WriteUInt32LE(outBuf, offset); WriteUInt32LE(outBuf, blockIndex);
            }
        }

        class GX2AttribVar {
            public string name = "";
            public uint type, arrayCount, location;
            public void Load(byte[] data, int pos, int shaderPos) {
                uint nameOffs = ReadUInt32BE(data, pos);
                type = ReadUInt32BE(data, pos + 4);
                arrayCount = ReadUInt32BE(data, pos + 8);
                location = ReadUInt32BE(data, pos + 12);
                name = ReadString(data, shaderPos + (int)(nameOffs & ~0xCA700000));
            }
            public void SaveLE(List<byte> outBuf, uint nameOffs) {
                WriteUInt32LE(outBuf, nameOffs); WriteUInt32LE(outBuf, type); WriteUInt32LE(outBuf, arrayCount); WriteUInt32LE(outBuf, location);
            }
        }

        class GX2SamplerVar {
            public string name = "";
            public uint type, location;
            public void Load(byte[] data, int pos, int shaderPos) {
                uint nameOffs = ReadUInt32BE(data, pos);
                type = ReadUInt32BE(data, pos + 4);
                location = ReadUInt32BE(data, pos + 8);
                name = ReadString(data, shaderPos + (int)(nameOffs & ~0xCA700000));
            }
            public void SaveLE(List<byte> outBuf, uint nameOffs) {
                WriteUInt32LE(outBuf, nameOffs); WriteUInt32LE(outBuf, type); WriteUInt32LE(outBuf, location);
            }
        }

        class GFDLoopVar {
            public uint offset, value;
        }

        class CompiledVertexShader {
            public uint[] regs = new uint[52];
            public byte[] shader = Array.Empty<byte>();
            public uint shaderMode, ringItemSize;
            public bool hasStreamOut;
            public uint[] streamOutStride = new uint[4];
            public GX2RBuffer rbuffer = new GX2RBuffer();

            public List<GX2UniformBlock> uniformBlocks = new List<GX2UniformBlock>();
            public List<GX2UniformVar> uniformVariables = new List<GX2UniformVar>();
            public List<GFDLoopVar> loopVariables = new List<GFDLoopVar>();
            public List<GX2SamplerVar> samplerVariables = new List<GX2SamplerVar>();
            public List<GX2AttribVar> attribVariables = new List<GX2AttribVar>();

            public void Load(byte[] data, int pos, byte[] shaderData) {
                int pos_ = pos;
                shader = shaderData;
                for (int i = 0; i < 52; i++) regs[i] = ReadUInt32BE(data, pos + i * 4);
                pos += 208; pos += 8;

                shaderMode = ReadUInt32BE(data, pos);
                uint numUniformBlocks = ReadUInt32BE(data, pos + 4), uniformBlocksOffs = ReadUInt32BE(data, pos + 8);
                uint numUniformVariables = ReadUInt32BE(data, pos + 12), uniformVariablesOffs = ReadUInt32BE(data, pos + 16);
                uint numInitialValues = ReadUInt32BE(data, pos + 20), initialValuesOffs = ReadUInt32BE(data, pos + 24);
                uint numLoopVariables = ReadUInt32BE(data, pos + 28), loopVariablesOffs = ReadUInt32BE(data, pos + 32);
                uint numSamplerVariables = ReadUInt32BE(data, pos + 36), samplerVariablesOffs = ReadUInt32BE(data, pos + 40);
                uint numAttribVariables = ReadUInt32BE(data, pos + 44), attribVariablesOffs = ReadUInt32BE(data, pos + 48);
                ringItemSize = ReadUInt32BE(data, pos + 52);
                hasStreamOut = ReadUInt32BE(data, pos + 56) != 0;
                pos += 60;

                for (int i=0; i<4; i++) streamOutStride[i] = ReadUInt32BE(data, pos + i*4);
                pos += 16;
                rbuffer.Load(data, pos);

                int p = pos_ + (int)(uniformBlocksOffs & ~0xD0600000);
                for(int i=0; i<numUniformBlocks; i++) { var b = new GX2UniformBlock(); b.Load(data, p, pos_); uniformBlocks.Add(b); p+=12; }
                p = pos_ + (int)(uniformVariablesOffs & ~0xD0600000);
                for(int i=0; i<numUniformVariables; i++) { var v = new GX2UniformVar(); v.Load(data, p, pos_); uniformVariables.Add(v); p+=20; }
                p = pos_ + (int)(loopVariablesOffs & ~0xD0600000);
                for(int i=0; i<numLoopVariables; i++) { loopVariables.Add(new GFDLoopVar { offset=ReadUInt32BE(data,p), value=ReadUInt32BE(data,p+4) }); p+=8; }
                p = pos_ + (int)(samplerVariablesOffs & ~0xD0600000);
                for(int i=0; i<numSamplerVariables; i++) { var s = new GX2SamplerVar(); s.Load(data, p, pos_); samplerVariables.Add(s); p+=12; }
                p = pos_ + (int)(attribVariablesOffs & ~0xD0600000);
                for(int i=0; i<numAttribVariables; i++) { var a = new GX2AttribVar(); a.Load(data, p, pos_); attribVariables.Add(a); p+=16; }
            }

            public byte[] SaveLE() {
                int offset = 308;
                uint uniformBlocksOffs = 0, uniformVariablesOffs = 0, loopVariablesOffs = 0, samplerVariablesOffs = 0, attribVariablesOffs = 0;

                if (uniformBlocks.Count > 0) { uniformBlocksOffs = (uint)offset; offset += uniformBlocks.Count * 12; }
                if (uniformVariables.Count > 0) { uniformVariablesOffs = (uint)offset; offset += uniformVariables.Count * 20; }
                if (samplerVariables.Count > 0) { samplerVariablesOffs = (uint)offset; offset += samplerVariables.Count * 12; }
                if (attribVariables.Count > 0) { attribVariablesOffs = (uint)offset; offset += attribVariables.Count * 16; }

                var strTable = new StringTable(offset);
                foreach(var b in uniformBlocks) strTable.Append(b.name);
                foreach(var v in uniformVariables) strTable.Append(v.name);
                foreach(var s in samplerVariables) strTable.Append(s.name);
                foreach(var a in attribVariables) strTable.Append(a.name);

                byte[] strTableB = strTable.Data.ToArray();
                offset += strTableB.Length;
                if (loopVariables.Count > 0) { loopVariablesOffs = (uint)offset; offset += loopVariables.Count * 8; }

                List<byte> outBuf = new List<byte>();
                for(int i=0; i<52; i++) WriteUInt32LE(outBuf, regs[i]);
                WriteUInt32LE(outBuf, (uint)shader.Length); WriteUInt32LE(outBuf, 0);
                WriteUInt32LE(outBuf, shaderMode);
                WriteUInt32LE(outBuf, (uint)uniformBlocks.Count); WriteUInt32LE(outBuf, uniformBlocksOffs);
                WriteUInt32LE(outBuf, (uint)uniformVariables.Count); WriteUInt32LE(outBuf, uniformVariablesOffs);
                WriteUInt32LE(outBuf, 0); WriteUInt32LE(outBuf, 0);
                WriteUInt32LE(outBuf, (uint)loopVariables.Count); WriteUInt32LE(outBuf, loopVariablesOffs);
                WriteUInt32LE(outBuf, (uint)samplerVariables.Count); WriteUInt32LE(outBuf, samplerVariablesOffs);
                WriteUInt32LE(outBuf, (uint)attribVariables.Count); WriteUInt32LE(outBuf, attribVariablesOffs);
                WriteUInt32LE(outBuf, ringItemSize); WriteUInt32LE(outBuf, hasStreamOut ? 1u : 0u);
                for(int i=0; i<4; i++) WriteUInt32LE(outBuf, streamOutStride[i]);
                rbuffer.SaveLE(outBuf);
                foreach(var b in uniformBlocks) b.SaveLE(outBuf, strTable.GetPos(b.name));
                foreach(var v in uniformVariables) v.SaveLE(outBuf, strTable.GetPos(v.name));
                foreach(var s in samplerVariables) s.SaveLE(outBuf, strTable.GetPos(s.name));
                foreach(var a in attribVariables) a.SaveLE(outBuf, strTable.GetPos(a.name));
                outBuf.AddRange(strTableB);
                foreach(var l in loopVariables) { WriteUInt32LE(outBuf, l.offset); WriteUInt32LE(outBuf, l.value); }
                return outBuf.ToArray();
            }
        }

        class CompiledPixelShader {
            public uint[] regs = new uint[41];
            public byte[] shader = Array.Empty<byte>();
            public uint shaderMode;
            public GX2RBuffer rbuffer = new GX2RBuffer();

            public List<GX2UniformBlock> uniformBlocks = new List<GX2UniformBlock>();
            public List<GX2UniformVar> uniformVariables = new List<GX2UniformVar>();
            public List<GFDLoopVar> loopVariables = new List<GFDLoopVar>();
            public List<GX2SamplerVar> samplerVariables = new List<GX2SamplerVar>();

            public void Load(byte[] data, int pos, byte[] shaderData) {
                int pos_ = pos;
                shader = shaderData;
                for (int i = 0; i < 41; i++) regs[i] = ReadUInt32BE(data, pos + i * 4);
                pos += 164; pos += 8;

                shaderMode = ReadUInt32BE(data, pos);
                uint numUniformBlocks = ReadUInt32BE(data, pos + 4), uniformBlocksOffs = ReadUInt32BE(data, pos + 8);
                uint numUniformVariables = ReadUInt32BE(data, pos + 12), uniformVariablesOffs = ReadUInt32BE(data, pos + 16);
                uint numInitialValues = ReadUInt32BE(data, pos + 20), initialValuesOffs = ReadUInt32BE(data, pos + 24);
                uint numLoopVariables = ReadUInt32BE(data, pos + 28), loopVariablesOffs = ReadUInt32BE(data, pos + 32);
                uint numSamplerVariables = ReadUInt32BE(data, pos + 36), samplerVariablesOffs = ReadUInt32BE(data, pos + 40);
                pos += 44;
                rbuffer.Load(data, pos);

                int p = pos_ + (int)(uniformBlocksOffs & ~0xD0600000);
                for(int i=0; i<numUniformBlocks; i++) { var b = new GX2UniformBlock(); b.Load(data, p, pos_); uniformBlocks.Add(b); p+=12; }
                p = pos_ + (int)(uniformVariablesOffs & ~0xD0600000);
                for(int i=0; i<numUniformVariables; i++) { var v = new GX2UniformVar(); v.Load(data, p, pos_); uniformVariables.Add(v); p+=20; }
                p = pos_ + (int)(loopVariablesOffs & ~0xD0600000);
                for(int i=0; i<numLoopVariables; i++) { loopVariables.Add(new GFDLoopVar { offset=ReadUInt32BE(data,p), value=ReadUInt32BE(data,p+4) }); p+=8; }
                p = pos_ + (int)(samplerVariablesOffs & ~0xD0600000);
                for(int i=0; i<numSamplerVariables; i++) { var s = new GX2SamplerVar(); s.Load(data, p, pos_); samplerVariables.Add(s); p+=12; }
            }

            public byte[] SaveLE() {
                int offset = 232;
                uint uniformBlocksOffs = 0, uniformVariablesOffs = 0, loopVariablesOffs = 0, samplerVariablesOffs = 0;

                if (uniformBlocks.Count > 0) { uniformBlocksOffs = (uint)offset; offset += uniformBlocks.Count * 12; }
                if (uniformVariables.Count > 0) { uniformVariablesOffs = (uint)offset; offset += uniformVariables.Count * 20; }
                if (samplerVariables.Count > 0) { samplerVariablesOffs = (uint)offset; offset += samplerVariables.Count * 12; }

                var strTable = new StringTable(offset);
                foreach(var b in uniformBlocks) strTable.Append(b.name);
                foreach(var v in uniformVariables) strTable.Append(v.name);
                foreach(var s in samplerVariables) strTable.Append(s.name);

                byte[] strTableB = strTable.Data.ToArray();
                offset += strTableB.Length;
                if (loopVariables.Count > 0) { loopVariablesOffs = (uint)offset; offset += loopVariables.Count * 8; }

                List<byte> outBuf = new List<byte>();
                for(int i=0; i<41; i++) WriteUInt32LE(outBuf, regs[i]);
                WriteUInt32LE(outBuf, (uint)shader.Length); WriteUInt32LE(outBuf, 0);
                WriteUInt32LE(outBuf, shaderMode);
                WriteUInt32LE(outBuf, (uint)uniformBlocks.Count); WriteUInt32LE(outBuf, uniformBlocksOffs);
                WriteUInt32LE(outBuf, (uint)uniformVariables.Count); WriteUInt32LE(outBuf, uniformVariablesOffs);
                WriteUInt32LE(outBuf, 0); WriteUInt32LE(outBuf, 0);
                WriteUInt32LE(outBuf, (uint)loopVariables.Count); WriteUInt32LE(outBuf, loopVariablesOffs);
                WriteUInt32LE(outBuf, (uint)samplerVariables.Count); WriteUInt32LE(outBuf, samplerVariablesOffs);
                rbuffer.SaveLE(outBuf);
                foreach(var b in uniformBlocks) b.SaveLE(outBuf, strTable.GetPos(b.name));
                foreach(var v in uniformVariables) v.SaveLE(outBuf, strTable.GetPos(v.name));
                foreach(var s in samplerVariables) s.SaveLE(outBuf, strTable.GetPos(s.name));
                outBuf.AddRange(strTableB);
                foreach(var l in loopVariables) { WriteUInt32LE(outBuf, l.offset); WriteUInt32LE(outBuf, l.value); }
                return outBuf.ToArray();
            }
        }

        static (byte[] vHeader, byte[] vData, byte[] pHeader, byte[] pData) ReadGFD(byte[] f) {
            uint magic = ReadUInt32BE(f, 0);
            if (magic != 0x47667832) throw new Exception("Invalid GFD magic"); // 'Gfx2'
            
            int pos = 32;
            byte[]? vHeader = null, vData = null, pHeader = null, pData = null;
            
            while(pos < f.Length) {
                uint bMagic = ReadUInt32BE(f, pos);
                if (bMagic != 0x424C4B7B) break; // End of blocks / padding
                
                uint bType = ReadUInt32BE(f, pos + 16);
                uint bDataSize = ReadUInt32BE(f, pos + 20);
                
                int dataPos = pos + 32; // Struct header is 32
                byte[] blockData = new byte[bDataSize];
                Array.Copy(f, dataPos, blockData, 0, bDataSize);
                
                if (bType == 3) { if (vHeader != null) throw new Exception("Multiple vertex shaders not supported"); vHeader = blockData; }
                else if (bType == 5) { if (vData != null) throw new Exception("Multiple vertex shaders not supported"); vData = blockData; }
                else if (bType == 6) { if (pHeader != null) throw new Exception("Multiple pixel shaders not supported"); pHeader = blockData; }
                else if (bType == 7) { if (pData != null) throw new Exception("Multiple pixel shaders not supported"); pData = blockData; }
                
                pos = dataPos + (int)bDataSize;
            }
            
            if (vHeader == null || vData == null || pHeader == null || pData == null) throw new Exception("Program missing shader data");
            return (vHeader, vData, pHeader, pData);
        }

        static byte[] CreateShaderBinary(int type, byte[] shaderStructBytes, byte[] shaderCodeBytes, int absolutePos) {
            int binaryPos = absolutePos + 16;
            int targetPos = (binaryPos + shaderStructBytes.Length + 0xFF) & ~0xFF;
            int padAmount = targetPos - binaryPos - shaderStructBytes.Length;
            byte[] padding = new byte[padAmount];

            int shaderOffs = shaderStructBytes.Length + padAmount;
            int regsLen = (type == 0) ? 208 : 164;
            shaderStructBytes[regsLen + 4] = (byte)(shaderOffs & 0xFF);
            shaderStructBytes[regsLen + 5] = (byte)((shaderOffs >> 8) & 0xFF);
            shaderStructBytes[regsLen + 6] = (byte)((shaderOffs >> 16) & 0xFF);
            shaderStructBytes[regsLen + 7] = (byte)((shaderOffs >> 24) & 0xFF);

            int totalBinaryLen = shaderStructBytes.Length + padAmount + shaderCodeBytes.Length;
            uint size = (uint)(16 + totalBinaryLen);

            List<byte> outBuf = new List<byte>();
            WriteUInt32LE(outBuf, size);
            WriteUInt32LE(outBuf, (uint)type);
            WriteUInt32LE(outBuf, 0); 
            WriteUInt32LE(outBuf, (uint)totalBinaryLen);
            outBuf.AddRange(shaderStructBytes);
            outBuf.AddRange(padding);
            outBuf.AddRange(shaderCodeBytes);
            return outBuf.ToArray();
        }

        static byte[] SaveSharcfbProgram(ShaderProgram p, int i) {
            byte[] nBytes = Encoding.UTF8.GetBytes(p.Name + "\0");
            byte[] vB = p.Variations.Save(), vsB = p.VariationDefaults.Save();
            byte[] uvB = p.UniformVariables.Save(), ubB = p.UniformBlocks.Save();
            byte[] svB = p.SamplerVariables.Save(), avB = p.AttribVariables.Save();

            uint size = (uint)(16 + nBytes.Length + vB.Length + vsB.Length + uvB.Length + ubB.Length + svB.Length + avB.Length);
            List<byte> outBuf = new List<byte>();
            WriteUInt32LE(outBuf, size);
            WriteUInt32LE(outBuf, (uint)nBytes.Length);
            WriteUInt32LE(outBuf, 3); // kind = 3
            WriteUInt32LE(outBuf, (uint)(i * 2)); // baseIndex
            outBuf.AddRange(nBytes);
            outBuf.AddRange(vB); outBuf.AddRange(vsB);
            outBuf.AddRange(uvB); outBuf.AddRange(ubB); outBuf.AddRange(svB); outBuf.AddRange(avB);
            return outBuf.ToArray();
        }

        static byte[] SaveSharcfbHeader(SharcHeader originalHeader) {
            byte[] nBytes = Encoding.UTF8.GetBytes(originalHeader.Name + "\0");
            List<byte> outBuf = new List<byte>();
            WriteUInt32LE(outBuf, 0x53484142); // SHAB
            WriteUInt32LE(outBuf, 8); // version
            WriteUInt32LE(outBuf, 0); // fileSize placeholder
            WriteUInt32LE(outBuf, 1); // endianness
            WriteUInt32LE(outBuf, 0); // 4 padding bytes
            WriteUInt32LE(outBuf, (uint)nBytes.Length);
            outBuf.AddRange(nBytes);
            return outBuf.ToArray();
        }

        static string ProcessMacros(string code, SharcList<ShaderMacro> macros) {
            var lines = code.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            for(int i = 0; i < lines.Length; i++) {
                if (lines[i].TrimStart().StartsWith("#define")) {
                    var parts = lines[i].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2) {
                        var mac = macros.Items.FirstOrDefault(m => m.Name == parts[1]);
                        if (mac != null) {
                            lines[i] = $"#define {mac.Name} {mac.Value}";
                        }
                    }
                }
            }
            return string.Join("\n", lines) + "\n";
        }

        public static void CompileAndSave(SharcArchive archive, string outputPath) {
            string tempDir = Path.Combine(Path.GetTempPath(), "SharcCompiler_" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try {
                var fbPrograms = new List<byte[]>();
                var fbBinaries = new List<Func<int, byte[]>>();

                for(int i = 0; i < archive.Programs.Items.Count; i++) {
                    var prog = archive.Programs.Items[i];
                    if (prog.VtxShIdx == -1 || prog.FrgShIdx == -1 || prog.GeoShIdx != -1) {
                        throw new Exception($"Invalid shader bindings for '{prog.Name}'. Vertex and Fragment indices must be mapped, Geometry must be unmapped (-1).");
                    }

                    var vtxSrc = archive.Codes.Items[prog.VtxShIdx];
                    var frgSrc = archive.Codes.Items[prog.FrgShIdx];

                    string vCode = ProcessMacros(vtxSrc.Code, prog.VertexMacros);
                    string fCode = ProcessMacros(frgSrc.Code, prog.FragmentMacros);

                    string vPath = Path.Combine(tempDir, vtxSrc.Name);
                    string fPath = Path.Combine(tempDir, frgSrc.Name);
                    File.WriteAllText(vPath, vCode);
                    File.WriteAllText(fPath, fCode);

                    string gshName = Path.Combine(tempDir, "out.gsh");
                    
                    var psi = new System.Diagnostics.ProcessStartInfo {
                        FileName = Program.gshCompilePath,
                        Arguments = $"-p \"{fPath}\" -v \"{vPath}\" -o \"{gshName}\" -no_limit_array_syms -nospark",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    var pProcess = System.Diagnostics.Process.Start(psi);
                    pProcess?.WaitForExit();

                    if (!File.Exists(gshName)) throw new Exception($"Compiler failed to output 'out.gsh' for '{prog.Name}'. Verify the shader code compiles correctly manually.");

                    byte[] gshData = File.ReadAllBytes(gshName);
                    var (vHeader, vData, pHeader, pData) = ReadGFD(gshData);

                    var compVtx = new CompiledVertexShader();
                    compVtx.Load(vHeader, 0, vData);
                    byte[] vStructBytes = compVtx.SaveLE();

                    var compPix = new CompiledPixelShader();
                    compPix.Load(pHeader, 0, pData);
                    byte[] pStructBytes = compPix.SaveLE();

                    fbBinaries.Add((absPos) => CreateShaderBinary(0, vStructBytes, compVtx.shader, absPos));
                    fbBinaries.Add((absPos) => CreateShaderBinary(1, pStructBytes, compPix.shader, absPos));

                    fbPrograms.Add(SaveSharcfbProgram(prog, i));
                }

                byte[] headerB = SaveSharcfbHeader(archive.Header);
                
                int binListStart = headerB.Length;
                List<byte> binListBuf = new List<byte>();
                WriteUInt32LE(binListBuf, 0); // size placeholder
                WriteUInt32LE(binListBuf, (uint)fbBinaries.Count);
                
                foreach(var binFunc in fbBinaries) {
                    int currentAbsPos = binListStart + binListBuf.Count;
                    byte[] binBytes = binFunc(currentAbsPos);
                    binListBuf.AddRange(binBytes);
                }
                
                uint binListSize = (uint)binListBuf.Count;
                binListBuf[0] = (byte)(binListSize & 0xFF);
                binListBuf[1] = (byte)((binListSize >> 8) & 0xFF);
                binListBuf[2] = (byte)((binListSize >> 16) & 0xFF);
                binListBuf[3] = (byte)((binListSize >> 24) & 0xFF);

                List<byte> progListBuf = new List<byte>();
                foreach(var p in fbPrograms) progListBuf.AddRange(p);
                uint progListSize = (uint)(8 + progListBuf.Count);
                List<byte> progListFull = new List<byte>();
                WriteUInt32LE(progListFull, progListSize);
                WriteUInt32LE(progListFull, (uint)fbPrograms.Count);
                progListFull.AddRange(progListBuf);

                List<byte> finalFile = new List<byte>();
                finalFile.AddRange(headerB);
                finalFile.AddRange(binListBuf);
                finalFile.AddRange(progListFull);

                byte[] outBytes = finalFile.ToArray();
                uint totalLen = (uint)outBytes.Length;
                outBytes[8] = (byte)(totalLen & 0xFF);
                outBytes[9] = (byte)((totalLen >> 8) & 0xFF);
                outBytes[10] = (byte)((totalLen >> 16) & 0xFF);
                outBytes[11] = (byte)((totalLen >> 24) & 0xFF);

                File.WriteAllBytes(outputPath, outBytes);

            } finally {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
