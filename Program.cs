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
        enum ConfirmAction { None, New, Open, Close, Exit }
        static ConfirmAction pendingAction = ConfirmAction.None;

        static void Main()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            GlfwWindowing.RegisterPlatform();
            GlfwInput.RegisterPlatform();
            
            var options = WindowOptions.Default;
            options.Size = new Silk.NET.Maths.Vector2D<int>(1440, 900);
            options.Title = "Shark";
            options.VSync = false;
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

            if (ImGui.Button("+ Program")) archive.Programs.Items.Add(new ShaderProgram { Name = "NewProgram" });
            ImGui.SameLine();
            if (ImGui.Button("+ Source")) archive.Codes.Items.Add(new ShaderSource { Name = "new_source.glsl", Code = "// Write GLSL here\n" });
            
            bool hasSelection = selectedIndex >= 0;
            if (hasSelection)
            {
                ImGui.SameLine();
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
        public static string? ShowOpenFileDialog()
        {
            if (OperatingSystem.IsWindows())
            {
                return RunProcess("powershell.exe", "-Command \"Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.OpenFileDialog; $f.Filter = 'Sharc Files (*.sharc)|*.sharc|All Files (*.*)|*.*'; $f.ShowHelp = $true; if($f.ShowDialog() -eq 'OK'){ $f.FileName }\"");
            }
            else if (OperatingSystem.IsLinux())
            {
                string? res = RunProcess("zenity", "--file-selection --title=\"Open Sharc Archive\"");
                if (string.IsNullOrEmpty(res)) res = RunProcess("kdialog", "--getopenfilename");
                return res;
            }
            else if (OperatingSystem.IsMacOS())
            {
                return RunProcess("osascript", "-e \"POSIX path of (choose file with prompt \\\"Select Sharc Archive\\\")\"");
            }
            return null;
        }

        public static string? ShowSaveFileDialog()
        {
            if (OperatingSystem.IsWindows())
            {
                return RunProcess("powershell.exe", "-Command \"Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.SaveFileDialog; $f.Filter = 'Sharc Files (*.sharc)|*.sharc|All Files (*.*)|*.*'; $f.ShowHelp = $true; if($f.ShowDialog() -eq 'OK'){ $f.FileName }\"");
            }
            else if (OperatingSystem.IsLinux())
            {
                string? res = RunProcess("zenity", "--file-selection --save --title=\"Save Sharc Archive\"");
                if (string.IsNullOrEmpty(res)) res = RunProcess("kdialog", "--getsavefilename");
                return res;
            }
            else if (OperatingSystem.IsMacOS())
            {
                return RunProcess("osascript", "-e \"POSIX path of (choose file name with prompt \\\"Save Sharc Archive As\\\")\"");
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
            Name = Encoding.UTF8.GetString(data, pos + 20, (int)nameLen).TrimEnd('\0');
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
            Name = Encoding.UTF8.GetString(data, p, (int)nameLen).TrimEnd('\0');
            p += (int)nameLen;
            Value = Encoding.UTF8.GetString(data, p, (int)valueLen).TrimEnd('\0');
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
            Name = Encoding.UTF8.GetString(data, p, (int)nameLen).TrimEnd('\0'); p += (int)nameLen;
            ID = Encoding.UTF8.GetString(data, p, (int)idLen).TrimEnd('\0'); p += (int)idLen;

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
            Name = Encoding.UTF8.GetString(data, p, (int)nameLen).TrimEnd('\0'); p += (int)nameLen;

            Values.Clear();
            for (int i = 0; i < vCount; i++)
            {
                while (data[p] == 0) p++;
                int s_pos = p++;
                while (data[p] != 0) p++;
                p++;
                Values.Add(Encoding.UTF8.GetString(data, s_pos, p - s_pos).TrimEnd('\0'));
            }
            ID = Encoding.UTF8.GetString(data, p, (int)idLen).TrimEnd('\0');
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
            Name = Encoding.UTF8.GetString(data, p, (int)nameLen).TrimEnd('\0'); p += (int)nameLen;
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
            Name = Encoding.UTF8.GetString(data, p, (int)nameLen).TrimEnd('\0'); p += (int)nameLen;

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
}
