using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 通用「当前画面 → 可编辑场景」烘焙工具(任意阶段复用:战斗/描绘/过场/结算)。
/// 用法两步:
///   A. 在目标画面 Play 中(如 调试→战斗,等背景出现)执行「通用A. 捕获当前画面」——
///      把运行时 stageRoot(漓江回声_关卡画面)底下整棵子树的每个物体(路径/位置/缩放/层级/
///      透明度/贴图)记录成 JSON。按"路径"记录,天然区分重名(装饰左手 vs 挥手左手)。
///   B. 退出 Play 后执行「通用B. 烘焙成可编辑场景」——读 JSON,新建一个场景,按原层级重建
///      每个图层(挂 SpriteRenderer + LijiangEchoSpriteLayer,贴图解析回资源),放一个预览相机,
///      让你选保存路径(每个阶段存一个 .unity)。之后可在 Scene 视图不 Play 直接拖拽摆位。
///
/// 说明:这是"可视化摆位"场景,静态背景准确;个别用了裁剪图(如"待描绘纹样")的图层会显示整图,
/// 属正常限制。运行时是否改用烘焙场景(双模式回填)是后续步骤,不在本工具内。
/// </summary>
public static class LijiangEchoSceneBakeTool
{
    private const string StageRootName = "漓江回声_关卡画面";
    private const string CapturePath = "ValidationCaptures/SceneBake_Last.json";
    private const string ArtSearchRoot = "Assets/Resources/LijiangEchoArt";

    [Serializable]
    public class Node
    {
        public string path;             // 相对 stageRoot 的层级路径,如 "怪物分层/怪物左翼"
        public Vector3 localPosition;
        public Vector3 localScale;
        public Vector3 localEuler;
        public bool hasSprite;
        public int sortingOrder;
        public float alpha;
        public string spriteAssetPath;
    }

    [Serializable]
    public class NodeSet
    {
        public string capturedAt;
        public List<Node> nodes = new List<Node>();
    }

    // ---------- A. 捕获(Play 中) ----------
    [MenuItem("漓江回声/场景化/通用A. 捕获当前画面(Play中)")]
    public static void CaptureCurrent()
    {
        if (!EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("需要 Play", "请先在目标画面(如 调试→战斗)进入 Play,等画面出现后再捕获。", "好");
            return;
        }

        Transform stageRoot = FindStageRoot();
        if (stageRoot == null)
        {
            EditorUtility.DisplayDialog("没找到画面根", $"场景里找不到「{StageRootName}」。请确认已进入某个阶段画面。", "好");
            return;
        }

        NodeSet set = new NodeSet { capturedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
        foreach (Transform t in stageRoot.GetComponentsInChildren<Transform>(true))
        {
            if (t == stageRoot)
            {
                continue;
            }

            SpriteRenderer sr = t.GetComponent<SpriteRenderer>();
            Node node = new Node
            {
                path = RelativePath(stageRoot, t),
                localPosition = t.localPosition,
                localScale = t.localScale,
                localEuler = t.localEulerAngles,
                hasSprite = sr != null,
                sortingOrder = sr != null ? sr.sortingOrder : 0,
                alpha = sr != null ? sr.color.a : 1f,
                spriteAssetPath = sr != null ? ResolveSpriteAssetPath(sr) : string.Empty
            };
            set.nodes.Add(node);
        }

        string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", CapturePath));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        File.WriteAllText(fullPath, JsonUtility.ToJson(set, true));

        int spriteCount = set.nodes.FindAll(n => n.hasSprite).Count;
        Debug.Log($"[漓江回声场景化] 已捕获 {set.nodes.Count} 个节点(含 {spriteCount} 个精灵图层)→ {CapturePath}");
        EditorUtility.DisplayDialog("捕获完成",
            $"已记录 {set.nodes.Count} 个节点(含 {spriteCount} 个图层)。\n\n退出 Play 后执行「通用B. 烘焙成可编辑场景」。", "好");
    }

    // ---------- B. 烘焙(退出 Play 后) ----------
    [MenuItem("漓江回声/场景化/通用B. 烘焙成可编辑场景(退出Play)")]
    public static void BakeToScene()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("请退出 Play", "烘焙会新建并保存场景,请先退出 Play 模式。", "好");
            return;
        }

        string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", CapturePath));
        if (!File.Exists(fullPath))
        {
            EditorUtility.DisplayDialog("没有捕获数据", "请先在 Play 中执行「通用A. 捕获当前画面」。", "好");
            return;
        }

        NodeSet set = JsonUtility.FromJson<NodeSet>(File.ReadAllText(fullPath));
        if (set == null || set.nodes.Count == 0)
        {
            EditorUtility.DisplayDialog("捕获数据为空", "重新执行「通用A. 捕获当前画面」。", "好");
            return;
        }

        string savePath = EditorUtility.SaveFilePanelInProject(
            "保存烘焙场景", "Battle_Background", "unity",
            "为这个阶段的可编辑场景选个保存位置/名字(每个阶段存一个)");
        if (string.IsNullOrEmpty(savePath))
        {
            return;
        }

        EnsureSpritesImported(set);

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        AddPreviewCamera();

        string rootName = Path.GetFileNameWithoutExtension(savePath);
        GameObject rootObject = new GameObject(rootName);
        rootObject.transform.position = Vector3.zero;
        rootObject.transform.rotation = Quaternion.identity;
        rootObject.transform.localScale = Vector3.one;

        // 父节点先于子节点创建:按路径深度排序。
        set.nodes.Sort((a, b) => Depth(a.path).CompareTo(Depth(b.path)));

        Dictionary<string, Transform> map = new Dictionary<string, Transform> { { string.Empty, rootObject.transform } };
        int spriteCreated = 0;
        int spriteMissing = 0;
        foreach (Node node in set.nodes)
        {
            string parentPath = ParentPath(node.path);
            Transform parent = map.TryGetValue(parentPath, out Transform p) ? p : rootObject.transform;

            GameObject go = new GameObject(LeafName(node.path));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = node.localPosition;
            go.transform.localEulerAngles = node.localEuler;
            go.transform.localScale = node.localScale;
            map[node.path] = go.transform;

            if (!node.hasSprite)
            {
                continue;
            }

            Sprite assetSprite = LoadSpriteAsset(node.spriteAssetPath);
            if (assetSprite == null)
            {
                spriteMissing++;
                continue; // 解析不到贴图(如运行时白块/裁剪图):只保留空节点,位置仍可编辑
            }

            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = assetSprite;
            renderer.sortingOrder = node.sortingOrder;
            renderer.color = new Color(1f, 1f, 1f, node.alpha);

            float assetWidth = assetSprite.bounds.size.x * Mathf.Abs(node.localScale.x);
            float assetHeight = assetSprite.bounds.size.y * Mathf.Abs(node.localScale.y);
            LijiangEchoSpriteLayer layer = go.AddComponent<LijiangEchoSpriteLayer>();
            layer.sprite = assetSprite;
            layer.sortingOrder = node.sortingOrder;
            layer.alpha = node.alpha;
            if (assetWidth >= assetHeight)
            {
                layer.fitMode = LijiangEchoSpriteLayer.FitMode.Width;
                layer.fitSize = assetWidth;
            }
            else
            {
                layer.fitMode = LijiangEchoSpriteLayer.FitMode.Height;
                layer.fitSize = assetHeight;
            }

            // 写回运行时数值,抵消 AddComponent 触发 Apply 的重算。
            go.transform.localPosition = node.localPosition;
            go.transform.localEulerAngles = node.localEuler;
            go.transform.localScale = node.localScale;
            spriteCreated++;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, savePath);

        Debug.Log($"[漓江回声场景化] 烘焙完成 → {savePath};图层 {spriteCreated} 个,未解析贴图 {spriteMissing} 个。");
        EditorUtility.DisplayDialog("烘焙完成",
            $"已生成场景:\n{savePath}\n\n图层 {spriteCreated} 个;{spriteMissing} 个节点未解析到贴图(空节点,位置仍可编辑)。\n" +
            "现在可以打开这个场景,在 Scene 视图直接拖拽摆位。", "好");
    }

    // ---------- 预览相机 ----------
    private static void AddPreviewCamera()
    {
        GameObject camObject = new GameObject("预览相机");
        Camera cam = camObject.AddComponent<Camera>();
        camObject.tag = "MainCamera";
        camObject.transform.position = new Vector3(0f, 0f, -8f);
        camObject.transform.rotation = Quaternion.identity; // 看向 +Z(内容在 z≈0 附近)
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.04f, 0.03f, 0.055f);
        cam.fieldOfView = 60f;
    }

    // ---------- 帮助函数 ----------
    private static Transform FindStageRoot()
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == StageRootName)
            {
                return root.transform;
            }

            Transform found = FindByNameRecursive(root.transform, StageRootName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static Transform FindByNameRecursive(Transform current, string name)
    {
        if (current.name == name)
        {
            return current;
        }

        foreach (Transform child in current)
        {
            Transform found = FindByNameRecursive(child, name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static string RelativePath(Transform root, Transform t)
    {
        string path = t.name;
        Transform cur = t.parent;
        while (cur != null && cur != root)
        {
            path = cur.name + "/" + path;
            cur = cur.parent;
        }

        return path;
    }

    private static int Depth(string path)
    {
        int count = 0;
        foreach (char c in path)
        {
            if (c == '/')
            {
                count++;
            }
        }

        return count;
    }

    private static string ParentPath(string path)
    {
        int idx = path.LastIndexOf('/');
        return idx < 0 ? string.Empty : path.Substring(0, idx);
    }

    private static string LeafName(string path)
    {
        int idx = path.LastIndexOf('/');
        return idx < 0 ? path : path.Substring(idx + 1);
    }

    private static string ResolveSpriteAssetPath(SpriteRenderer renderer)
    {
        if (renderer == null || renderer.sprite == null || renderer.sprite.texture == null)
        {
            return string.Empty;
        }

        Texture2D texture = renderer.sprite.texture;
        string path = AssetDatabase.GetAssetPath(texture);
        if (!string.IsNullOrEmpty(path))
        {
            return path;
        }

        string textureName = texture.name;
        if (string.IsNullOrEmpty(textureName))
        {
            return string.Empty;
        }

        foreach (string guid in AssetDatabase.FindAssets(textureName + " t:Texture2D", new[] { ArtSearchRoot }))
        {
            string candidate = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(candidate) == textureName)
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private static void EnsureSpritesImported(NodeSet set)
    {
        HashSet<string> paths = new HashSet<string>();
        foreach (Node node in set.nodes)
        {
            if (node.hasSprite && !string.IsNullOrEmpty(node.spriteAssetPath))
            {
                paths.Add(node.spriteAssetPath);
            }
        }

        bool anyReimported = false;
        foreach (string path in paths)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                continue;
            }

            bool changed = false;
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                changed = true;
            }

            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                anyReimported = true;
            }
        }

        if (anyReimported)
        {
            AssetDatabase.Refresh();
        }
    }

    private static Sprite LoadSpriteAsset(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
        {
            return null;
        }

        foreach (UnityEngine.Object item in AssetDatabase.LoadAllAssetsAtPath(assetPath))
        {
            if (item is Sprite sprite)
            {
                return sprite;
            }
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
    }
}
