using CtrLibrary;
using SPICA.PICA.Shader;
using Syroot.BinaryData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Toolbox.Core;
using Toolbox.Core.IO;
using Toolbox.Core.ViewModels;
using UIFramework;
using CtrLibrary.UI;
using ByamlExt.Byaml;
using Newtonsoft.Json.Linq;
using ImGuiNET;
using MapStudio.UI;
using System.Numerics;

namespace CtrLibrary
{
    public class BYAML : MapStudio.UI.FileEditor, IFileFormat
    {
        public bool CanSave { get; set; } = true;

        public string[] Description { get; set; } = new string[] { "byaml" };
        public string[] Extension { get; set; } = new string[] { "*.byaml" };

        public File_Info FileInfo { get; set; }

        public bool Identify(File_Info fileInfo, Stream stream)
        {
            using (FileReader reader = new FileReader(stream, true)) {
                return reader.CheckSignature(2, "BY") || reader.CheckSignature(2, "YB");
            }
        }

        /// <summary>
        /// Prepares the dock layouts to be used for the file format.
        /// </summary>
        public override List<DockWindow> PrepareDocks()
        {
            List<DockWindow> windows = new List<DockWindow>();
            windows.Add(Workspace.Outliner);
            windows.Add(Workspace.PropertyWindow);
            return windows;
        }

        public static Dictionary<uint, string> Hashes = new Dictionary<uint, string>();

        private ByamlEditorUI Editor;

        public void Load(Stream stream)
        {
            var Byaml = ByamlFile.LoadN(stream);
            Editor = new ByamlEditorUI();
            Editor.LoadRoot(Root, Byaml, FileInfo.FileName);
        }

        public void Save(Stream stream)
        {
            ByamlFile.SaveN(stream, Editor.BymlFile);
        }

        public static bool IsHash(string hash)
        {
            return false;
        }

        public override void DrawArchiveFileEditor()
        {
            Editor.RenderEditor();
        }

        public override string ToTextEditor()
        {
            return YamlByamlConverter.ToYaml(Editor.BymlFile);
        }

        public override void SaveTextEditor(string text)
        {
            Editor.BymlFile = YamlByamlConverter.FromYaml(text);
        }
    }

    class ByamlEditorUI
    {
        public BymlFileData BymlFile;

        public class ByamlNode : TreeNode
        {
            private List<int> SelectedIndices = new List<int>();

            public ByamlNode(string name)
            {
                Header = name;
                Icon = "";
                this.ContextMenus.Add(new MenuItem("Copy Node Text", CopyNodeText));
                this.ContextMenus.Add(new MenuItem("Add Node", CopyNodeText));
                this.ContextMenus.Add(new MenuItem("Edit Note", () => { }) { Enabled = false, });
                this.ContextMenus.Add(new MenuItem("Delete Node", DeleteNode));
                this.ContextMenus.Add(new MenuItem(""));
                this.ContextMenus.Add(new MenuItem("Export", Export));
                this.ContextMenus.Add(new MenuItem("Import", Replace));
                this.ContextMenus.Add(new MenuItem(""));
                this.ContextMenus.Add(new MenuItem("Save As", SaveAs));
            }

            private void CopyNodeText()
            {
                Clipboard.Copy(this.Header);
            }

            private void DeleteNode()
            {

            }

            private void Export()
            {

            }

            private void Replace()
            {

            }

            private void SaveAs()
            {

            }

            public override void OnBeforeExpand()
            {
                dynamic value = this.Tag;
                if (this.Children.Count == 1 && this.Children[0].Header == "<<dummy>>")
                {
                    this.Children.Clear();

                    if (value is IDictionary<string, dynamic>)
                        LoadDictionary(value, this);
                    else if (value is IList<dynamic>)
                        LoadArray(value, this);
                }
            }

            public void DrawUI()
            {
                if (this.Tag is IDictionary<string, dynamic>)
                    DrawDictProperties((IDictionary<string, dynamic>)this.Tag);
                if (this.Tag is IList<dynamic>)
                    DrawArrayProperties((IList<dynamic>)this.Tag);
            }

            private void DrawArrayProperties(IList<dynamic> node)
            {
                ImGui.BeginColumns("byamlPropertiesClm", 2);

                ImGuiHelper.BoldText("Data");
                ImGui.NextColumn();

                ImGuiHelper.BoldText("Type");
                ImGui.NextColumn();

                ImGui.EndColumns();

                ImGui.PushStyleColor(ImGuiCol.ChildBg, ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg]);

                if (ImGui.BeginChild("list"))
                {
                    ImGui.BeginColumns("byamlPropertiesClm", 2);

                    int index = 0;
                    foreach (var v in node)
                    {
                        //Skip lists and dictionaries
                        if ((v is IDictionary<string, dynamic>) ||
                            (v is List<dynamic>) ||
                            (v is List<ByamlPathPoint>))
                        {
                            continue;
                        }

                        bool selected = SelectedIndices.Contains(index);

                        //Display text
                        string valueText = (v == null ? "<NULL>" : v.ToString());
                        string typeText = (v == null ? "NULL" : v.GetType().ToString());

                        typeText = typeText.Replace("System.", "");

                        if (ImGui.Selectable(valueText, selected, ImGuiSelectableFlags.SpanAllColumns))
                        {
                            SelectedIndices.Clear();
                            SelectedIndices.Add(index);
                        }
                        ImGui.NextColumn();
                        ImGui.Text(typeText);
                        ImGui.NextColumn();

                        if (ImGui.BeginPopupContextItem("##Edit", ImGuiPopupFlags.MouseButtonRight))
                        {
                            if (ImGui.MenuItem("Edit"))
                            {
                                
                            }
                            ImGui.EndPopup();
                        }

                        index++;
                    }

                    ImGui.EndColumns();
                }
                ImGui.EndChild();

                ImGui.PopStyleColor();
            }

            private void DrawDictProperties(IDictionary<string, dynamic> node)
            {
                ImGui.BeginColumns("byamlPropertiesClm", 3);

                ImGuiHelper.BoldText("Name");
                ImGui.NextColumn();

                ImGuiHelper.BoldText("Data");
                ImGui.NextColumn();

                ImGuiHelper.BoldText("Type");
                ImGui.NextColumn();

                ImGui.EndColumns();

                ImGui.PushStyleColor(ImGuiCol.ChildBg, ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg]);

                if (ImGui.BeginChild("list"))
                {
                    ImGui.BeginColumns("byamlPropertiesClm", 3);


                    int index = 0;
                    foreach (var k in node.Keys)
                    {
                        //Skip lists and dictionaries
                        if ((node[k] is IDictionary<string, dynamic>) ||
                            (node[k] is List<dynamic>) ||
                            (node[k] is List<ByamlPathPoint>))
                        {
                            continue;
                        }

                        bool selected = SelectedIndices.Contains(index);

                        //Display text
                        string nameText = k;
                        string valueText = (node[k] == null ? "<NULL>" : node[k].ToString());
                        string typeText = (node[k] == null ? "NULL" : node[k].GetType().ToString());

                        typeText = typeText.Replace("System.", "");

                        if (ImGui.Selectable(nameText, selected, ImGuiSelectableFlags.SpanAllColumns))
                        {
                            SelectedIndices.Clear();
                            SelectedIndices.Add(index);
                        }
                        bool doubleClick = ImGui.IsMouseDoubleClicked(0) && ImGui.IsItemHovered();

                        ImGui.NextColumn();
                        ImGui.Text(valueText); ImGui.NextColumn();
                        ImGui.Text(typeText); ImGui.NextColumn();

                        if (doubleClick)
                            DrawEditDialog(k, node[k]);;

                        if (ImGui.BeginPopupContextItem("##Edit", ImGuiPopupFlags.MouseButtonRight))
                        {
                            if (ImGui.MenuItem("Edit"))
                            {
                                DrawEditDialog(k, node[k]);
                            }
                            ImGui.EndPopup();
                        }

                        index++;
                    }

                    ImGui.EndColumns();
                }
                ImGui.EndChild();

                ImGui.PopStyleColor();
            }

            private void DrawEditDialog(string text, dynamic value)
            {
                DialogHandler.Show("Byaml Edit", () =>
                {
                    EditDialog(text, value);
                }, (ok) =>
                {
                    if (!ok)
                        return;

                }, 300, 80);
            }

            private void EditDialog(dynamic value)
            {

            }

            private void EditDialog(string text, dynamic value)
            {
                ImGuiHelper.DrawCenteredText($"Type: {value.GetType()}");

                string name = text;

                var width = ImGui.GetWindowWidth() / 2;

                ImGui.PushItemWidth(width - 3);
                if (ImGui.InputText("##Name", ref name, 0x100))
                {
                    text = name;
                }

                ImGui.SameLine();
                ImGui.PopItemWidth();

                ImGui.PushItemWidth(width - 3);

                if (value is string)
                {
                    string str = value;
                    if (ImGui.InputText("##Value", ref str, 0x100))
                        value = str;
                }
                else if (value is float)
                {
                    float v = value;
                    if (ImGui.DragFloat("##Value", ref v))
                        value = v;
                }
                else if (value is long)
                {
                    string v = ((long)value).ToString();
                    if (ImGui.InputText("##Value", ref v, 0x100))
                    {
                        if (long.TryParse(v, out long d))
                        {

                        }
                    }
                }
                else if (value is ulong)
                {
                    string v = ((ulong)value).ToString();
                    if (ImGui.InputText("##Value", ref v, 0x100))
                    {
                        if (ulong.TryParse(v, out ulong d))
                        {

                        }
                    }
                }
                else if (value is double)
                {
                    string v = ((double)value).ToString();
                    if (ImGui.InputText("##Value", ref v, 0x100))
                    {
                        if (double.TryParse(v, out double d))
                        {

                        }
                    }
                }
                else if (value is int)
                {
                    int v = value;
                    if (ImGui.DragInt("##Value", ref v))
                        value = v;
                }
                else if (value is uint)
                {
                    int v = (int)(uint)value;
                    if (ImGui.DragInt("##Value", ref v))
                        value = (uint)v;
                }
                else if (value is bool)
                {
                    bool v = (bool)value;
                    if (ImGui.Checkbox("##Value", ref v))
                        value = v;
                }
                ImGui.PopItemWidth();
            }
        }

        TreeView Tree = new TreeView();

        static bool ShiftJIS = false;

        private TextFormatting Formatting = TextFormatting.Yaml;

        enum TextFormatting
        {
            Yaml,
            Xml,
        }

        private float FontScale = 1.0f;

        private string FileName;

        public void LoadRoot(NodeBase parent, BymlFileData byaml, string fileName)
        {
            FileName = fileName;
                
            Reload(byaml);

            parent.TagUI.UIDrawer += delegate
            {
                RenderEditor();
            };
        }

        public void RenderEditor()
        {
            ImGui.Checkbox("Use Shift JIS-Encoding", ref ShiftJIS);

            ImGui.BeginTabBar("byamlTab");

            if (ImguiCustomWidgets.BeginTab("byamlTab", "Editor"))
            {
                DrawEditorUI();
                ImGui.EndTabItem();
            }
            if (ImguiCustomWidgets.BeginTab("byamlTab", "Text Editor"))
            {
                DrawTextEditorUI();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        public void ReloadEncoding()
        {
        }

        private void Reload(BymlFileData byaml)
        {
            TreeNode n = new TreeNode(FileName);
            n.Icon = "";

            Tree.DeselectAll();
            Tree.Nodes.Clear();
            Tree.Nodes.Add(n);

            BymlFile = byaml;

            var root = byaml.RootNode;

            //the first node should always be a dictionary node
            if (root is IDictionary<string, dynamic>)
                LoadDictionary(root, n);
            else if (root is List<dynamic>)
                LoadArray(root, n);
            else throw new Exception($"Unsupported root node type {root.GetType()}");
        }

        ImguiTextEditor TextEditor = new ImguiTextEditor();

        public void DrawTextEditorUI()
        {
            if (ImGui.BeginChild("menuStrip", new Vector2(ImGui.GetWindowWidth(), 19), false, ImGuiWindowFlags.MenuBar))
            {
                ImGui.BeginMenuBar();
                if (ImGui.MenuItem("Decompile"))
                {
                    Decompile();
                }
                if (ImGui.MenuItem("Compile"))
                {
                    if (!string.IsNullOrEmpty(TextEditor.Text))
                    {
                        try
                        {
                            switch (Formatting)
                            {
                                case TextFormatting.Yaml:
                                    BymlFile = YamlByamlConverter.FromYaml(TextEditor.Text);
                                    break;
                                case TextFormatting.Xml:
                                    BymlFile = XmlByamlConverter.FromXML(TextEditor.Text);
                                    break;
                            }
                        }
                        catch (Exception ex)
                        {
                            TinyFileDialog.MessageBoxErrorOk(ex.ToString());
                        }
                        Reload(BymlFile);
                    }
                }
                if (ImGui.BeginMenu("Change Formatting"))
                {
                    if (ImGui.MenuItem("Yaml", "", this.Formatting == TextFormatting.Yaml))
                    {
                        this.Formatting = TextFormatting.Yaml;
                        Decompile();
                    }
                    if (ImGui.MenuItem("Xml", "", this.Formatting == TextFormatting.Xml))
                    {
                        this.Formatting = TextFormatting.Xml;
                        Decompile();
                    }
                    ImGui.EndMenu();
                }
                ImGui.EndMenuBar();
            }
            ImGui.EndChild();

            ImGui.PushStyleColor(ImGuiCol.ChildBg, ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg]);

            if (ImGui.BeginChild("textEditor"))
            {
                TextEditor.Render();
            }
            ImGui.EndChild();

            ImGui.PopStyleColor();
        }

        private void Decompile()
        {
            switch (Formatting)
            {
                case TextFormatting.Yaml:
                    TextEditor.Load(YamlByamlConverter.ToYaml(BymlFile));
                    break;
                case TextFormatting.Xml:
                    TextEditor.Load(XmlByamlConverter.ToXML(BymlFile));
                    break;
            }
        }

        private void DrawEditorUI()
        {
            var style = ImGui.GetStyle();
            var frameSize = style.FramePadding;
            var itemSpacing = style.ItemSpacing;

            style.ItemSpacing = (new Vector2(itemSpacing.X, 2));
            style.FramePadding = (new Vector2(frameSize.X, 2));

            ImGui.BeginColumns("byamlEditorClm", 2);

            Tree.Render();

            ImGui.NextColumn();

            if (ImGui.BeginChild("nodeList"))
            {
                foreach (var node in Tree.GetSelectedNodes())
                {
                    if (node is ByamlNode)
                        ((ByamlNode)node).DrawUI();
                }
            }
            ImGui.EndChild();

            ImGui.NextColumn();

            ImGui.EndColumns();

            style.FramePadding = frameSize;
            style.ItemSpacing = itemSpacing;
        }

        private static void LoadArray(IList<dynamic> node, TreeNode parent)
        {
            int index = 0;
            foreach (var n in node)
            {
                if ((n is IDictionary<string, dynamic>) ||
                    (n is List<dynamic>) ||
                    (n is List<ByamlPathPoint>))
                {
                    parent.AddChild(LoadListNode(n, index));
                    index++;
                }
            }
        }

        private static void LoadDictionary(IDictionary<string, dynamic> node, TreeNode parent)
        {
            int index = 0;
            foreach (var n in node)
            {
                if ((n.Value is IDictionary<string, dynamic>) ||
                    (n.Value is List<dynamic>) ||
                    (n.Value is List<ByamlPathPoint>))
                {
                    parent.AddChild(LoadListNode(n.Value, index, n.Key));
                    index++;
                }
            }
        }

        private static TreeNode LoadListNode(dynamic v, int index, string text = "")
        {
            TreeNode current = new TreeNode();

            if (v is IDictionary<string, dynamic>)
            {
                string name = string.IsNullOrEmpty(text) ? $"<Dictionary> {index}" : $"{text} : <Dictionary> {index}";
                current = new ByamlNode(name);
                current.Tag = ((IDictionary<string, dynamic>)v);

                if (HasDynamicListChildren(current))
                    current.AddChild(new TreeNode("<<dummy>>"));
            }
            else if (v is IList<dynamic>)
            {
                string name = string.IsNullOrEmpty(text) ? $"<Array> {index}" : $"{text} : <Array> {index}";
                current = new ByamlNode(name);
                current.Tag = ((IList<dynamic>)v);

                if (HasDynamicListChildren(current))
                    current.AddChild(new TreeNode("<<dummy>>"));
            }
            else if (v is IList<ByamlPathPoint>)
            {
                current = new ByamlNode($"<PathPointArray> {index}");
                current.Tag = ((IList<ByamlPathPoint>)v);
                parsePathPointArray(current, v, current.Children);
            }
            else
                throw new Exception($"Unknown type {v}");

            return current;
        }

        static void parsePathPointArray(TreeNode parent, IList<ByamlPathPoint> list, IEnumerable<TreeNode> children)
        {
            int index = 0;
            foreach (var k in list)
            {
                index++;

                var n = (k == null ? new TreeNode("<NULL>") : new TreeNode(k.ToString()));
                parent.AddChild(n);

                if (k != null) n.Tag = k;
            }
        }

        //Search through the properties of a dictionary or list and see if it contains a list/dictionary
        //Then use this information to add tree nodes.
        //This is so nodes can be added on click but visually have children
        static bool HasDynamicListChildren(TreeNode Node)
        {
            if (Node.Tag != null)
            {
                if (((dynamic)Node.Tag).Count > 0)
                {
                    if (Node.Tag is IList<dynamic>)
                        return ListHasListChild((IList<dynamic>)Node.Tag);
                    if (Node.Tag is IDictionary<string, dynamic>)
                        return DictionaryHasListChild((IDictionary<string, dynamic>)Node.Tag);
                }
            }

            return false;
        }

        static bool ListHasListChild(IList<dynamic> list)
        {
            foreach (dynamic k in list)
            {
                if (k is IDictionary<string, dynamic>)
                    return true;
                else if (k is IList<dynamic>)
                    return true;
            }
            return false;
        }

        static bool DictionaryHasListChild(IDictionary<string, dynamic> node)
        {
            foreach (string k in node.Keys)
            {
                if (node[k] is IDictionary<string, dynamic>)
                    return true;
                else if (node[k] is IList<dynamic>)
                    return true;
            }
            return false;
        }
    }
}
