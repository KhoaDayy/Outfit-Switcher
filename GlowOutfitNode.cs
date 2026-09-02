using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using Warudo.Core;
using Warudo.Core.Attributes;
using Warudo.Core.Graphs;
using Warudo.Plugins.Core.Assets.Character;

namespace Warudo.Plugins.McpBridge.Nodes {

    /// <summary>
    /// GLOW OUTFIT — hiệu ứng đổi đồ "đồ 1 flash tới peak → biến mất → đồ 2
    /// hiện ra với flash ở peak hạ dần", chạy trên Warudo BiRP, không shader
    /// tự viết, không đụng material gốc.
    ///
    /// Cách hoạt động: với mỗi Renderer dưới các GameObjectPath, tạo một bản
    /// sao mesh (overlay) dùng shader Particles/Additive (BiRP có sẵn). Độ
    /// sáng mã hóa theo ĐỘ CAO từng đỉnh (vertex color) → vệt sáng quét từ
    /// dưới lên trên. Nhịp hiệu ứng:
    ///
    ///   1. GLOW UP (đồ 1): overlay copy của đồ 1 sáng dần, toàn bộ object.
    ///   2. PEAK:           trắng xóa. Cùng frame: onPeak() swap SetActive,
    ///                      overlay đồ 1 bị HỦY và overlay mới được TẠO LẠI từ
    ///                      mesh của đồ 2 (swapPaths) — sinh ra ngay ở độ sáng
    ///                      đỉnh, phủ đúng silhouette đồ mới (flash > đồ 2).
    ///   3. GLOW DOWN (đồ 2): overlay đồ 2 hạ từ peak: trắng → GLOW COLOR →
    ///                      tan về 0, lộ đồ 2 bình thường.
    ///
    /// Nếu không truyền swapPaths → overlay đồ 1 fade nốt sau peak (hành vi
    /// cũ, dùng cho toggle-off hoặc glow đơn thuần).
    ///
    /// KHÔNG lọc renderer disabled/inactive khi gom: glow có thể trỏ vào
    /// outfit đang tắt (sắp được bật) — overlay tự render độc lập nhờ treo
    /// ở root avatar + bones tham chiếu trực tiếp.
    ///
    /// Nếu mesh không đọc được (isReadable=false) → fallback flash đều qua
    /// _TintColor (mất sweep dưới→trên, còn đầy đủ peak/swap/fade).
    ///
    /// Phương thức Glow(...) là static — dùng chung cho blueprint,
    /// SwitchGroupNode và lệnh MCP glow_outfit.
    /// </summary>
    [NodeType(
        Id = "3f8c1a2e-6d5b-4f7a-9c2e-8b1a4d6f9c3e",
        Title = "GLOW OUTFIT",
        Category = "Hasukatsu"
    )]
    public class GlowOutfitNode : Node {

        // ── Cancel/debounce: mỗi (character, glowKey) cho phép 1 glow chạy ──
        // Cho phép glow nhiều group/item độc lập trên cùng character chạy song song.
        // Chỉ debounce/cancel khi cùng character VÀ cùng glowKey (vd cùng group/item).
        private static readonly Dictionary<(CharacterAsset, string), CancellationTokenSource> _activeGlows
            = new Dictionary<(CharacterAsset, string), CancellationTokenSource>();

        // ── Shader cache — Shader.Find không rẻ, gọi 1 lần duy nhất ──
        private static Shader _cachedAdditiveShader;

        [DataInput]
        [Label("CHARACTER")]
        public CharacterAsset Character;

        [DataInput]
        [Label("GAMEOBJECT PATHS")]
        [Description("Path tới outfit/thân cần glow (vd Assets/Outfits/SuriMukeki). KHÔNG trỏ vào mặt/tóc che mặt để giữ biểu cảm.")]
        public string[] GameObjectPaths = new[] { "Assets/Outfits/SuriMukeki" };

        [DataInput]
        [Label("GLOW COLOR")]
        [Description("Màu glow ở pha đầu và pha tan. Ở đỉnh lóa overlay chuyển sang TRẮNG.")]
        public Color GlowColor = new Color(1f, 0.4f, 0.8f, 1f);

        [DataInput]
        [Label("INTENSITY")]
        [Description("Độ chói tại đỉnh. Nên 3~4 để trắng xóa che khuất cú swap.")]
        public float Intensity = 3f;

        [DataInput]
        [Label("EXTRUSION (M)")]
        [Description("Độ phồng overlay theo pháp tuyến đỉnh (mặc định 0.005 = 5mm).")]
        public float Extrusion = 0.005f;

        [DataInput]
        [Label("DURATION (MS)")]
        public int DurationMs = 600;

        [DataInput]
        [Label("PEAK (0-1)")]
        [Description("Thời điểm lóa cực đại tính theo tỉ lệ Duration (0.42 ≈ 0.25s với 600ms). Cú cắt đổi đồ xảy ra đúng lúc này.")]
        public float PeakPercent = 0.42f;

        [DataInput]
        [Label("SWAP GAMEOBJECT PATHS (ĐỒ MỚI)")]
        [Description("Path tới outfit MỚI sẽ hiện sau peak. Nếu có, tại peak overlay đồ cũ bị hủy và overlay đồ mới được tạo từ các path này — đồ mới lộ ra với glow hạ dần (đúng timeline swap). Để trống = overlay đồ cũ fade nốt (không glow đồ mới).")]
        public string[] SwapGameObjectPaths;

        [DataInput]
        [Label("DEBUG LOGS")]
        [Description("Bật/tắt Debug.Log chi tiết. Tắt khi stream để tránh spam console.")]
        public bool DebugLogs = false;

        [FlowInput]
        public Continuation Enter() {
            var key = GameObjectPaths != null && GameObjectPaths.Length > 0 ? "node:" + string.Join(";", GameObjectPaths) : "node:default";
            Glow(Character, GameObjectPaths ?? Array.Empty<string>(),
                GlowColor, Intensity, DurationMs, PeakPercent,
                onPeak: () => {
                    try {
                        InvokeFlow(nameof(OnPeak), false);
                    } catch (Exception e) {
                        Debug.LogWarning("[GlowOutfit] InvokeFlow thất bại: " + e.Message);
                    }
                },
                swapPaths: SwapGameObjectPaths,
                glowKey: key,
                flushOnCancel: false,
                extrusion: Extrusion,
                debugLog: DebugLogs).Forget();
            return Exit; // flow chính không bị chặn — glow chạy nền
        }

        [FlowOutput]
        [Label("EXIT (IMMEDIATE)")]
        public Continuation Exit;

        [FlowOutput]
        [Label("SWAP OUTFIT (AT PEAK)")]
        [Description("Kích hoạt đúng frame lóa trắng cực đại — nối các node TOGGLE outfit vào đây để match cut bị ánh sáng che hoàn toàn.")]
        public Continuation OnPeak;

        // ── Overlay tạm của một renderer nguồn ──
        private class Overlay {
            public GameObject Go;                 // GameObject chứa overlay (để hủy khi swap tại peak)
            public SkinnedMeshRenderer SourceSmr; // để sync blendshape (null nếu MeshRenderer)
            public Renderer SourceMr;             // để sync transform nếu là MeshRenderer
            public Renderer Renderer;             // renderer của overlay
            public Mesh MeshCopy;                 // null nếu mesh gốc không đọc được
            public float[] Heights;               // null → fallback uniform (không sweep được)
            public Color[] Colors;
            public Material Mat;
        }

        /// <summary>
        /// Chạy glow transformation (dùng chung: blueprint + SwitchGroup + MCP).
        /// swapPaths: nếu có, TẠI PEAK overlay cũ bị hủy và overlay mới được
        /// tạo từ renderer dưới swapPaths (đồ mới) — flash phủ đúng silhouette
        /// bộ mới trong pha tan. Gọi onPeak TRƯỚC khi rebuild nên bộ mới đã
        /// active khi overlay của nó được tạo.
        /// glowKey: key phân biệt các glow khác nhau trên cùng character (vd groupName hoặc itemPath).
        /// flushOnCancel: nếu true, khi bị cancel trước peak sẽ lập tức gọi onPeak để hoàn tất logic trạng thái.
        /// </summary>
        public static async UniTask Glow(CharacterAsset character, string[] paths,
                Color color, float intensity, int durationMs, float peakPercent,
                Action onPeak, string[] swapPaths = null, string glowKey = "",
                bool flushOnCancel = false, float extrusion = 0.005f, bool debugLog = false) {
            if (character?.GameObject == null) return;

            // Cancel glow cũ có CÙNG key của character này (nếu có) trước khi start mới
            // → độc lập giữa các group/item khác nhau, debounce khi spam cùng item/group.
            var cancelKey = (character, glowKey ?? "");
            CancellationTokenSource cts;
            lock (_activeGlows) {
                if (_activeGlows.TryGetValue(cancelKey, out var old)) {
                    old.Cancel();
                    old.Dispose();
                }
                cts = new CancellationTokenSource();
                _activeGlows[cancelKey] = cts;
            }
            var ct = cts.Token;

            var peak = Mathf.Clamp(peakPercent, 0.05f, 0.95f);
            var duration = Math.Max(100, durationMs) / 1000f;

            var overlays = new List<Overlay>();
            var spawned = new List<UnityEngine.Object>();
            var peakFired = false;

            try {
                // Kiểm tra cancel ngay đầu
                if (ct.IsCancellationRequested) return;

                // 1. Container treo vào ROOT avatar — độc lập với outfit bị toggle
                var container = new GameObject("GlowOutfit_Overlays");
                container.hideFlags = HideFlags.DontSave;
                container.transform.SetParent(character.GameObject.transform, false);
                spawned.Add(container);

                // 2. Overlay của bộ HIỆN TẠI (đồ 1)
                BuildOverlays(character, paths, container.transform, overlays, spawned, extrusion, debugLog);
                if (overlays.Count == 0) {
                    if (debugLog) Debug.LogWarning("[GlowOutfit] Không tạo được overlay nào cho đồ cũ, gọi onPeak để swap.");
                    peakFired = true;
                    onPeak?.Invoke();
                    return;
                }

                // 3. Animate: quét dưới→trên, TRẮNG XÓA tại peak, glow màu tan dần
                var elapsed = 0f;
                const float edge = 0.25f; // độ mềm của mép sáng dẫn đầu
                while (elapsed < duration) {
                    // Safety: cancel nếu bị trigger lại hoặc character bị destroy giữa chừng
                    if (ct.IsCancellationRequested || character?.GameObject == null) return;

                    var t = Mathf.Clamp01(elapsed / duration);
                    var isSweepPhase = t <= peak;

                    // Envelope: lên nhanh tới peak, xuống chậm sau peak.
                    // KHÔNG reset khi swap overlay → overlay đồ 2 sinh ra đúng
                    // lúc env=1 (đỉnh) rồi tự hạ dần theo nhịp chung.
                    var env = isSweepPhase
                        ? Smooth01(t / peak)
                        : 1f - Smooth01((t - peak) / (1f - peak));

                    // Mặt sáng quét từ dưới (-0.15) lên quá đỉnh (1.15) trong pha
                    // lên; sau peak giữ nguyên → toàn thân sáng đều rồi fade
                    var sweep = Mathf.Lerp(-0.15f, 1.15f, Mathf.Clamp01(t / peak));

                    if (!peakFired && t >= peak) {
                        peakFired = true;
                        onPeak?.Invoke(); // ← MATCH CUT: SetActive swap tại đây

                        // Đổi overlay sang mesh đồ MỚI: hủy overlay đồ 1 (nó
                        // "biến mất"), tạo overlay đồ 2 — sinh ra ở env=1,
                        // trắng xóa, rồi hạ dần cùng envelope.
                        if (swapPaths != null && swapPaths.Length > 0) {
                            foreach (var ov in overlays) {
                                if (ov.Go != null) { spawned.Remove(ov.Go); UnityEngine.Object.Destroy(ov.Go); }
                                if (ov.Mat != null) { spawned.Remove(ov.Mat); UnityEngine.Object.Destroy(ov.Mat); }
                                if (ov.MeshCopy != null) { spawned.Remove(ov.MeshCopy); UnityEngine.Object.Destroy(ov.MeshCopy); }
                            }
                            overlays.Clear();
                            BuildOverlays(character, swapPaths, container.transform, overlays, spawned, extrusion, debugLog);
                            if (overlays.Count == 0 && debugLog) {
                                Debug.LogWarning("[GlowOutfit] Swap overlay: 0 renderer tìm thấy — kiểm tra lại SWAP GAMEOBJECT PATHS!");
                            }
                        } else {
                            // Không swap: gán toàn bộ colors thành Color.white 1 lần duy nhất cho pha glow down
                            foreach (var ov in overlays) {
                                if (ov.MeshCopy != null && ov.Colors != null) {
                                    for (var i = 0; i < ov.Colors.Length; i++) ov.Colors[i] = Color.white;
                                    ov.MeshCopy.colors = ov.Colors;
                                }
                            }
                        }
                    }

                    // Hai pha màu: env cao (quanh đỉnh) → TRẮNG; env thấp (pha tan) → glow color
                    var whiteMix = Mathf.Clamp01((env - 0.55f) / 0.45f);
                    var c = Color.Lerp(color, Color.white, whiteMix);
                    var b = env * intensity;
                    var glowCol = new Color(c.r * b, c.g * b, c.b * b, Mathf.Clamp01(b));
                    var tintAdditive = new Color(glowCol.r * 0.5f, glowCol.g * 0.5f, glowCol.b * 0.5f, glowCol.a * 0.5f);
                    var neutralAdditive = new Color(0.5f, 0.5f, 0.5f, 0.5f);

                    foreach (var ov in overlays) {
                        if (ov.Renderer == null) continue;

                        if (isSweepPhase && ov.Heights != null && ov.MeshCopy != null) {
                            // Sweep mode: quét dưới lên trên theo vertex color (chỉ upload GPU trong pha sweep)
                            for (var i = 0; i < ov.Heights.Length; i++) {
                                var band = Mathf.Clamp01((sweep + edge - ov.Heights[i]) / edge);
                                var fill = Mathf.Lerp(0.35f, 1f, band);
                                var vb = env * fill * intensity;
                                ov.Colors[i] = new Color(c.r * vb, c.g * vb, c.b * vb, Mathf.Clamp01(vb));
                            }
                            ov.MeshCopy.colors = ov.Colors;
                        }

                        // Cập nhật màu Material cho mọi overlay
                        if (ov.Mat != null) {
                            var isAdditive = ov.Mat.shader != null && ov.Mat.shader.name.IndexOf("Additive", StringComparison.OrdinalIgnoreCase) >= 0;
                            if (isSweepPhase && ov.Heights != null && ov.MeshCopy != null) {
                                // Pha sweep: vertex color đã chứa màu & độ sáng -> tint để trung tính
                                if (ov.Mat.HasProperty("_TintColor")) ov.Mat.SetColor("_TintColor", isAdditive ? neutralAdditive : Color.white);
                                if (ov.Mat.HasProperty("_Color")) ov.Mat.SetColor("_Color", Color.white);
                            } else {
                                // Pha glow down hoặc fallback non-readable: điều khiển độ sáng trực tiếp qua Material
                                if (ov.Mat.HasProperty("_TintColor")) ov.Mat.SetColor("_TintColor", isAdditive ? tintAdditive : glowCol);
                                if (ov.Mat.HasProperty("_Color")) ov.Mat.SetColor("_Color", glowCol);
                            }
                        }

                        // Outfit gốc có thể đã bị tắt tại peak — overlay phải tự sống nốt
                        if (!ov.Renderer.enabled) ov.Renderer.enabled = true;
                        // Sync blendshape để overlay không tách khỏi mesh gốc giữa chừng
                        if (ov.SourceSmr != null && ov.Renderer is SkinnedMeshRenderer smrDup && ov.SourceSmr.sharedMesh != null) {
                            for (var i = 0; i < ov.SourceSmr.sharedMesh.blendShapeCount; i++)
                                smrDup.SetBlendShapeWeight(i, ov.SourceSmr.GetBlendShapeWeight(i));
                        }
                        // Sync transform & scale cho MeshRenderer tĩnh
                        if (ov.SourceMr != null && ov.Go != null) {
                            ov.Go.transform.position = ov.SourceMr.transform.position;
                            ov.Go.transform.rotation = ov.SourceMr.transform.rotation;
                            var pLossy = container.transform.lossyScale;
                            var sLossy = ov.SourceMr.transform.lossyScale;
                            ov.Go.transform.localScale = new Vector3(
                                pLossy.x > 1e-6f ? sLossy.x / pLossy.x : 1f,
                                pLossy.y > 1e-6f ? sLossy.y / pLossy.y : 1f,
                                pLossy.z > 1e-6f ? sLossy.z / pLossy.z : 1f
                            );
                        }
                    }

                    try {
                        await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    } catch (OperationCanceledException) {
                        return;
                    }
                    elapsed += UnityEngine.Time.deltaTime;
                }
            }
            finally {
                // Nếu bị cancel trước khi kịp swap/toggle tại peak và có cờ flushOnCancel -> hoàn tất cú swap ngay lập tức
                if (!peakFired && flushOnCancel) {
                    try {
                        onPeak?.Invoke();
                    } catch (Exception e) {
                        if (debugLog) Debug.LogWarning("[GlowOutfit] Flush on cancel failed: " + e.Message);
                    }
                }

                // 4. Dọn dẹp — material gốc chưa từng bị đụng, chỉ hủy overlay.
                foreach (var o in spawned) if (o != null) UnityEngine.Object.Destroy(o);

                // Xóa CTS khỏi dict nếu nó vẫn là instance của mình (chưa bị
                // thay thế bởi lần glow mới). Dispose CTS.
                lock (_activeGlows) {
                    if (_activeGlows.TryGetValue(cancelKey, out var current) && current == cts) {
                        _activeGlows.Remove(cancelKey);
                    }
                }
                cts.Dispose();
            }
        }

        /// <summary>
        /// Gom renderer dưới các path rồi tạo overlay cho từng renderer.
        /// KHÔNG lọc inactive — glow có thể nhắm vào outfit đang tắt (sắp bật).
        /// </summary>
        private static void BuildOverlays(CharacterAsset character, string[] paths,
                Transform container, List<Overlay> overlays, List<UnityEngine.Object> spawned,
                float extrusion = 0.005f, bool debugLog = false) {
            var seen = new HashSet<Renderer>();
            foreach (var path in paths ?? Array.Empty<string>()) {
                if (string.IsNullOrWhiteSpace(path)) continue;
                var go = FindGameObjectByPath(character.GameObject, path);
                if (go == null) {
                    if (debugLog) Debug.LogWarning("[GlowOutfit] path NOT FOUND: " + path);
                    continue;
                }
                var renderers = go.GetComponentsInChildren<Renderer>(true);
                if (debugLog)
                    Debug.Log($"[GlowOutfit] path='{path}' → GO='{go.name}' renderers={renderers.Length}");
                foreach (var r in renderers) {
                    if (r == null) continue;
                    if (r.name.EndsWith("_GlowOverlay")) continue;
                    if (!seen.Add(r)) continue; // đã tạo overlay cho renderer này rồi
                    var ov = CreateOverlay(r, container, extrusion, debugLog);
                    if (ov == null) continue;
                    overlays.Add(ov);
                    spawned.Add(ov.Go);
                    spawned.Add(ov.Mat);
                    if (ov.MeshCopy != null) spawned.Add(ov.MeshCopy);
                }
            }
        }

        /// <summary>
        /// Tạo bản sao additive của renderer. Nếu mesh đọc được → copy mesh +
        /// mã hóa độ cao vào vertex color (sweep dưới→trên). Nếu không → dùng
        /// mesh gốc, flash đều bằng tint/color.
        /// </summary>
        private static Overlay CreateOverlay(Renderer src, Transform parent, float extrusion = 0.005f, bool debugLog = false) {
            Mesh mesh = null;
            if (src is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
            else if (src is MeshRenderer) mesh = src.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) return null;

            Mesh useMesh = mesh;
            Mesh copy = null;
            float[] heights = null;
            Color[] colors = null;

            // Thử copy mesh và tính heights cho vertex-color sweep
            try {
                if (mesh.isReadable) {
                    copy = UnityEngine.Object.Instantiate(mesh);
                    copy.name = mesh.name + "_GlowCopy";

                    // Gán UV đầy đủ để shader sample được whiteTexture
                    var uv = new Vector2[copy.vertexCount];
                    for (var i = 0; i < uv.Length; i++) uv[i] = new Vector2(0.5f, 0.5f);
                    copy.uv = uv;
                    try { copy.SetUVs(1, uv); } catch { }

                    var verts = copy.vertices;
                    if (copy.normals == null || copy.normals.Length != verts.Length) {
                        copy.RecalculateNormals();
                    }
                    var normals = copy.normals;

                    // Extrude nhẹ các đỉnh ra ngoài theo pháp tuyến (mặc định 5mm) để glow nổi bao bọc bên ngoài layer gốc, triệt tiêu hoàn toàn Z-fighting
                    if (normals != null && normals.Length == verts.Length && extrusion > 0f) {
                        for (var i = 0; i < verts.Length; i++) {
                            verts[i] += normals[i] * extrusion;
                        }
                        copy.vertices = verts;
                    }

                    var b = copy.bounds;
                    var min = b.min.y;
                    var range = Mathf.Max(1e-5f, b.max.y - b.min.y);
                    heights = new float[verts.Length];
                    colors = new Color[verts.Length];
                    for (var i = 0; i < verts.Length; i++) {
                        heights[i] = (verts[i].y - min) / range;
                        colors[i] = Color.white;
                    }
                    copy.colors = colors;
                    copy.RecalculateBounds();
                    useMesh = copy;
                }
            } catch (Exception e) {
                if (debugLog) Debug.LogWarning($"[GlowOutfit] Mesh copy failed for '{src.name}', using fallback: {e.Message}");
                copy = null;
                heights = null;
                colors = null;
                useMesh = mesh;
            }

            if (_cachedAdditiveShader == null) {
                _cachedAdditiveShader = Shader.Find("Particles/Additive")
                                     ?? Shader.Find("Legacy Shaders/Particles/Additive")
                                     ?? Shader.Find("Mobile/Particles/Additive")
                                     ?? Shader.Find("UI/Default");
            }
            if (_cachedAdditiveShader == null) {
                Debug.LogWarning("[GlowOutfit] Không tìm thấy shader phù hợp!");
                return null;
            }
            if (debugLog) Debug.Log($"[GlowOutfit] Resolved Shader: {_cachedAdditiveShader.name}");

            var ov = new Overlay();
            ov.Heights = heights;
            ov.Colors = colors;
            ov.MeshCopy = copy;

            // Dùng chung Particles/Additive cho cả readable và fallback non-readable
            ov.Mat = new Material(_cachedAdditiveShader) { name = "GlowOutfit_Mat", hideFlags = HideFlags.DontSave };
            ov.Mat.renderQueue = 4000; // Overlay queue để render sau cùng toàn bộ renderers của avatar
            if (ov.Mat.HasProperty("_InvFade")) ov.Mat.SetFloat("_InvFade", 10000f);
            ov.Mat.DisableKeyword("SOFTPARTICLES_ON");
            ov.Mat.mainTexture = Texture2D.whiteTexture;
            if (ov.Mat.HasProperty("_MainTex")) ov.Mat.SetTexture("_MainTex", Texture2D.whiteTexture);
            if (ov.Mat.HasProperty("_ZWrite")) ov.Mat.SetInt("_ZWrite", 0);
            if (ov.Mat.HasProperty("_ZTest")) ov.Mat.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            if (ov.Mat.HasProperty("_TintColor")) ov.Mat.SetColor("_TintColor", Color.white);
            if (ov.Mat.HasProperty("_Color")) ov.Mat.SetColor("_Color", Color.white);

            var go = new GameObject(src.name + "_GlowOverlay");
            go.hideFlags = HideFlags.DontSave;
            go.layer = src.gameObject.layer; // Copy chính xác layer của renderer gốc để tránh bị camera culling mask loại trừ
            go.transform.SetParent(parent, false);
            go.transform.position = src.transform.position;
            go.transform.rotation = src.transform.rotation;
            var pScale = parent.lossyScale;
            var sScale = src.transform.lossyScale;
            go.transform.localScale = new Vector3(
                pScale.x > 1e-6f && sScale.x > 1e-6f ? sScale.x / pScale.x : 1f,
                pScale.y > 1e-6f && sScale.y > 1e-6f ? sScale.y / pScale.y : 1f,
                pScale.z > 1e-6f && sScale.z > 1e-6f ? sScale.z / pScale.z : 1f
            );
            ov.Go = go;

            var subCount = Mathf.Max(1, useMesh.subMeshCount);
            var mats = new Material[subCount];
            for (var i = 0; i < subCount; i++) mats[i] = ov.Mat;

            if (src is SkinnedMeshRenderer srcSmr) {
                var dup = go.AddComponent<SkinnedMeshRenderer>();
                dup.sharedMesh = useMesh;
                dup.bones = srcSmr.bones;
                dup.rootBone = srcSmr.rootBone;
                dup.updateWhenOffscreen = true;
                dup.localBounds = new Bounds(Vector3.zero, Vector3.one * 100f);
                if (srcSmr.sharedMesh != null)
                    for (var i = 0; i < srcSmr.sharedMesh.blendShapeCount; i++)
                        dup.SetBlendShapeWeight(i, srcSmr.GetBlendShapeWeight(i));
                dup.sharedMaterials = mats;
                ov.SourceSmr = srcSmr;
                ov.Renderer = dup;
            } else {
                go.AddComponent<MeshFilter>().sharedMesh = useMesh;
                var dup = go.AddComponent<MeshRenderer>();
                dup.sharedMaterials = mats;
                ov.Renderer = dup;
                ov.SourceMr = src;
            }
            ov.Renderer.shadowCastingMode = ShadowCastingMode.Off;
            ov.Renderer.receiveShadows = false;
            return ov;
        }

        private static float Smooth01(float x) {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        /// <summary>
        /// Resolve path: tìm chính xác GameObject trên avatar.
        /// </summary>
        public static GameObject FindGameObjectByPath(GameObject root, string path) {
            if (root == null || string.IsNullOrWhiteSpace(path)) return null;

            // 1. Direct Transform.Find
            var t = root.transform.Find(path);
            if (t != null) return t.gameObject;

            // 2. Thử bỏ tên root nếu path bắt đầu bằng root.name
            if (path.StartsWith(root.name + "/", StringComparison.OrdinalIgnoreCase)) {
                var sub = path.Substring(root.name.Length + 1);
                t = root.transform.Find(sub);
                if (t != null) return t.gameObject;
            }

            // 3. Tìm chính xác theo relative path từ root
            var cleanPath = path.Trim('/');
            var hit = SearchByExactRelativePath(root.transform, cleanPath, root.transform);
            if (hit != null) return hit.gameObject;

            // 4. Fallback: tìm theo tên object cuối cùng (Leaf Name)
            var lastSlash = cleanPath.LastIndexOf('/');
            var leafName = lastSlash >= 0 ? cleanPath.Substring(lastSlash + 1) : cleanPath;
            hit = SearchByName(root.transform, leafName);
            return hit != null ? hit.gameObject : null;
        }

        private static Transform SearchByExactRelativePath(Transform current, string targetPath, Transform root) {
            foreach (Transform child in current) {
                var rel = OutfitSwitcherAsset.GetRelativePath(root, child);
                if (string.Equals(rel, targetPath, StringComparison.OrdinalIgnoreCase)) {
                    return child;
                }
                var found = SearchByExactRelativePath(child, targetPath, root);
                if (found != null) return found;
            }
            return null;
        }

        private static Transform SearchByName(Transform current, string leafName) {
            foreach (Transform child in current) {
                if (string.Equals(child.name, leafName, StringComparison.OrdinalIgnoreCase)) {
                    return child;
                }
                var found = SearchByName(child, leafName);
                if (found != null) return found;
            }
            return null;
        }
    }
}
