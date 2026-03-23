using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using UniVRM10;
using colloid.VRM10Ex.Utility;

namespace colloid.VRM10Ex
{
	[CustomEditor(typeof(VRM10SpringBoneEx))][CanEditMultipleObjects]
	public class VRM10SpringBoneExEditor : Editor
	{
		[SerializeField]
		VisualTreeAsset m_springBoneExUI, m_springBoneExColliderGroupsList;

		VRM10SpringBoneEx m_instance;
		Vrm10Instance m_VRM10Instance;

		SerializedObject m_VRMInstance;
		string m_spring;

		// MultipleEdit
		VRM10SpringBoneEx[] m_instances;
		string[] m_springs;

		// Undo/Redo でUI値を再読み込みするコールバック
		Action m_refreshUI;

		// SerializedObjectキャッシュ（bindItem内でのリーク防止）
		readonly Dictionary<UnityEngine.Object, SerializedObject> m_serializedObjectCache = new Dictionary<UnityEngine.Object, SerializedObject>();

		SerializedObject GetOrCreateSerializedObject(UnityEngine.Object obj)
		{
			if (!m_serializedObjectCache.TryGetValue(obj, out var so) || so == null)
			{
				so = new SerializedObject(obj);
				m_serializedObjectCache[obj] = so;
			}
			return so;
		}

		void DisposeSerializedObjectCache()
		{
			foreach (var so in m_serializedObjectCache.Values)
				so?.Dispose();
			m_serializedObjectCache.Clear();
		}

		protected void OnEnable()
		{
			if (m_springBoneExUI == null)
				m_springBoneExUI = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AssetDatabase.GUIDToAssetPath("513d600b93ba1304e8347be91dd549e0"));
			if (m_springBoneExColliderGroupsList == null)
				m_springBoneExColliderGroupsList = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AssetDatabase.GUIDToAssetPath("e1ce3a43555952b47b50c0fe89b56c37"));
			var ex = this.target as VRM10SpringBoneEx;
			if (ex != null) ex.Init();
			Init();
			Undo.undoRedoPerformed += OnUndoRedo;
		}

		protected void OnDestroy()
		{
			Undo.undoRedoPerformed -= OnUndoRedo;
			m_VRMInstance?.Dispose();
			DisposeSerializedObjectCache();

			if (this.target != null) return;
			if (m_VRM10Instance == null) return;

			m_instance?.DestroyImmediate();
		}

		void OnUndoRedo()
		{
			// Undo/Redo 後に状態を再取得
			if (m_instance != null) m_instance.Init();
			Init();
			m_refreshUI?.Invoke();
			SceneView.RepaintAll();

			// Targetが破棄されていた場合、Inspector全体を再構築
			if (m_instance != null && m_instance.Target == null)
			{
				EditorApplication.delayCall += () =>
				{
					if (m_instance != null)
						ActiveEditorTracker.sharedTracker.ForceRebuild();
				};
			}
		}

		void OnSceneGUI()
		{
			if (m_VRM10Instance == null) return;

			// アクティブインスタンスのハンドルのみ描画
			if (m_instance == null || m_instance.Target == null) return;
			if (VRM10SpringBoneEx.ActiveInstance != null && VRM10SpringBoneEx.ActiveInstance != m_instance) return;

			// m_instance.Spring を直接使用（共有ルートでも正しい Spring を描画）
			var spring = m_instance.Spring;
			if (spring == null || spring.Joints == null || spring.Joints.Count == 0) return;

			// ラベル
			SpringBoneGizmoDrawer.DrawLabel(
				spring.Name, m_instance.SpringIndex,
				m_instance.Target.name, 0,
				m_instance.Target.transform.position);

			// AngleLimit + Space（全 Joint を描画）
			for (int j = 0; j < spring.Joints.Count - 1; j++)
			{
				var jointJ = spring.Joints[j];
				var jointJ1 = spring.Joints[j + 1];
				if (jointJ == null || jointJ1 == null) continue;
				SpringBoneGizmoDrawer.DrawAngleLimitAndSpace(
					jointJ, jointJ.transform, jointJ1.transform);
			}
		}

		public override VisualElement CreateInspectorGUI()
		{
			var root = new VisualElement();

			// VRM10Instance存在チェック
			if (m_VRM10Instance == null)
			{
				root.Add(new HelpBox("VRM10 Instance が見つかりません。VRM ヒエラルキー内に配置してください。", HelpBoxMessageType.Error));
				return root;
			}

			m_springBoneExUI.CloneTree(root);

			// アクティブインスタンス追跡: クリックでこのEditorをアクティブに
			root.RegisterCallback<PointerDownEvent>(evt =>
			{
				VRM10SpringBoneEx.ActiveInstance = m_instance;
				SceneView.RepaintAll();
			});
			VRM10SpringBoneEx.ActiveInstance = m_instance;

			// --- 静的UI バインディング ---

			// Spring Name（Springが設定されている場合のみバインド）
			var springName = root.Q<TextField>("SpringName");
			if (m_spring != null)
			{
				springName.BindProperty(m_VRMInstance.FindProperty($"{m_spring}.Name"));
				springName.RegisterValueChangedCallback(evt =>
				{
					m_instance.Name = evt.newValue;
				});
			}
			else
			{
				springName.SetEnabled(false);
				springName.SetValueWithoutNotify("(未設定)");
			}

			// UI要素取得
			var rootBone = root.Q<ObjectField>("RootBone");
			var multiChildField = root.Q<EnumField>("MultiChildType");
			var setupMessage = root.Q<HelpBox>("SetupMessage");
			var branchSelector = root.Q<VisualElement>("BranchSelector");
			var generateBranchesBtn = root.Q<Button>("GenerateBranchesButton");
			var regenerateBtn = root.Q<Button>("RegenerateButton");
			var duplicateWarning = root.Q<HelpBox>("DuplicateWarning");
			var dynamicFieldsContainer = root.Q<VisualElement>("DynamicFieldsContainer");

			rootBone.SetEnabled(m_instances.Length < 2);
			rootBone.SetValueWithoutNotify(m_instance.Target?.transform);

			// MultiChildType EnumField 初期化
			multiChildField.Init(m_instance.MultiChild);
			multiChildField.style.display = DisplayStyle.None; // 分岐検出時のみ表示

			// Spring依存UIの表示/非表示
			bool hasSpring = m_spring != null;
			var colliderGroupsList = root.Q<ListView>();
			var centerField = root.Q<ObjectField>("Center");
			colliderGroupsList.style.display = hasSpring ? DisplayStyle.Flex : DisplayStyle.None;
			centerField.style.display = hasSpring ? DisplayStyle.Flex : DisplayStyle.None;

			// UI表示状態の更新ヘルパー
			void UpdateSetupVisibility()
			{
				bool hasTarget = m_instance.Target != null;
				setupMessage.style.display = hasTarget ? DisplayStyle.None : DisplayStyle.Flex;
				regenerateBtn.style.display = hasTarget ? DisplayStyle.Flex : DisplayStyle.None;
				branchSelector.style.display = DisplayStyle.None;
				generateBranchesBtn.style.display = DisplayStyle.None;
				multiChildField.style.display = DisplayStyle.None;
			}
			UpdateSetupVisibility();

			// チェーン収集＋BranchSelector更新のヘルパー
			void UpdateBranchUI(Transform targetRoot)
			{
				if (targetRoot == null) return;
				var chains = VRM10SpringBoneEx.CollectChains(targetRoot, m_instance.MultiChild);
				if (chains.Count > 1)
				{
					BuildBranchSelector(branchSelector, chains);
					multiChildField.style.display = DisplayStyle.Flex;
					branchSelector.style.display = DisplayStyle.Flex;
					generateBranchesBtn.style.display = DisplayStyle.Flex;
					setupMessage.style.display = DisplayStyle.None;
					regenerateBtn.style.display = DisplayStyle.None;
				}
				else if (chains.Count == 1)
				{
					// モードによって1チェーンになる場合（枝分かれはあるがIgnoreで1チェーンなど）
					multiChildField.style.display = DisplayStyle.Flex;
					branchSelector.style.display = DisplayStyle.None;
					generateBranchesBtn.style.display = DisplayStyle.Flex;
					setupMessage.style.display = DisplayStyle.None;
					regenerateBtn.style.display = DisplayStyle.None;
				}
			}

			// MultiChildType ValueChanged
			multiChildField.RegisterValueChangedCallback(evt =>
			{
				Undo.RecordObject(m_instance, "Change Multi Child Type");
				m_instance.MultiChild = (MultiChildType)evt.newValue;
				EditorUtility.SetDirty(m_instance);

				var currentRoot = rootBone.value as Transform;
				if (currentRoot != null)
					UpdateBranchUI(currentRoot);
			});

			// RootBone ValueChanged
			rootBone.RegisterValueChangedCallback(evt =>
			{
				var newTransform = evt.newValue as Transform;
				if (newTransform == null)
				{
					// クリア
					if (m_instance.SpringIndex >= 0 && m_instance.Spring?.Joints != null)
						m_VRM10Instance.SpringBone.Springs[m_instance.SpringIndex].Joints.Clear();
					setupMessage.style.display = DisplayStyle.Flex;
					regenerateBtn.style.display = DisplayStyle.None;
					branchSelector.style.display = DisplayStyle.None;
					generateBranchesBtn.style.display = DisplayStyle.None;
					multiChildField.style.display = DisplayStyle.None;
					RebuildDynamicFields(dynamicFieldsContainer);
					return;
				}

				// 重複チェック
				CheckDuplicateSpring(newTransform, duplicateWarning);

				// 枝分かれの有無を確認（フルパスで判定）
				var fullChains = VRM10SpringBoneEx.CollectChains(newTransform);
				if (fullChains.Count > 1)
				{
					// 枝分かれあり: MultiChildType + BranchSelector を表示
					UpdateBranchUI(newTransform);
				}
				else
				{
					// 枝分かれなし: 即座に生成
					multiChildField.style.display = DisplayStyle.None;
					m_instance.GenerateJoints(newTransform);
					EditorUtility.SetDirty(m_instance);
					EditorUtility.SetDirty(m_VRM10Instance);
					Init();
					ActiveEditorTracker.sharedTracker.ForceRebuild();
				}
			});

			// Generate Selected（枝分かれ生成）
			generateBranchesBtn.clicked += () =>
			{
				var selectedChains = GetSelectedChains(branchSelector);
				if (selectedChains.Count == 0) return;

				// 共通プレフィックス長を計算（Spring名の分岐点特定用）
				int commonLen = 0;
				if (selectedChains.Count > 1)
				{
					int minLen = selectedChains.Min(c => c.Count);
					for (int i = 0; i < minLen; i++)
					{
						if (selectedChains.All(c => c[i] == selectedChains[0][i]))
							commonLen = i + 1;
						else break;
					}
				}

				Undo.IncrementCurrentGroup();
				int undoGroup = Undo.GetCurrentGroup();

				bool first = true;
				foreach (var chain in selectedChains)
				{
					// 共通部分以降の最初のTransform名をSpring名に使用
					int nameIdx = Math.Min(commonLen, chain.Count - 1);
					var chainName = chain[nameIdx].name;

					if (first)
					{
						// 最初のチェーンは現在のSpringBoneExを使用
						m_instance.GenerateJoints(chain, chainName);
						first = false;
					}
					else
					{
						// 追加チェーンは同じGameObjectにSpringBoneExを追加
						var newEx = Undo.AddComponent<VRM10SpringBoneEx>(m_instance.gameObject);
						newEx.GenerateJoints(chain, chainName);
					}
				}

				Undo.CollapseUndoOperations(undoGroup);

				EditorUtility.SetDirty(m_instance);
				EditorUtility.SetDirty(m_VRM10Instance);

				Init();
				// Inspector全体を再構築
				ActiveEditorTracker.sharedTracker.ForceRebuild();
			};

			// Regenerate Joints（再生成）
			regenerateBtn.clicked += () =>
			{
				var currentRoot = m_instance.Target?.transform;
				if (currentRoot == null) return;

				// springNameなし → 既存Springを再利用
				m_instance.GenerateJoints(currentRoot);
				EditorUtility.SetDirty(m_instance);
				EditorUtility.SetDirty(m_VRM10Instance);
				Init();
				RebuildDynamicFields(dynamicFieldsContainer);
			};

			// --- Spring依存UIのバインディング（Springが設定されている場合のみ）---
			if (hasSpring)
			{
				// ColliderGroups ListView
				colliderGroupsList.BindProperty(m_VRMInstance.FindProperty($"{m_spring}.ColliderGroups"));
				colliderGroupsList.makeItem = () =>
				{
					var ve = m_springBoneExColliderGroupsList.CloneTree();
					ve.Q<ObjectField>().RegisterValueChangedCallback(evt =>
					{
						if (evt.newValue == null)
						{
							ve.Q<TextField>().Unbind();
							ve.Q<ListView>().Unbind();
							ve.Q<ListView>().makeItem = () => new ObjectField() { objectType = typeof(VRM10SpringBoneCollider) };
							return;
						}
						ve.Q<TextField>().BindProperty(GetOrCreateSerializedObject(evt.newValue).FindProperty("Name"));
						var list = ve.Q<ListView>();
						list.BindProperty(GetOrCreateSerializedObject(evt.newValue).FindProperty("Colliders"));
					});
					return ve;
				};
				colliderGroupsList.bindItem = (ve, i) =>
				{
					ve.Q<ObjectField>().BindProperty(m_VRMInstance.FindProperty($"{m_spring}.ColliderGroups.Array.data[{i}]"));
					if (ve.Q<ObjectField>().value == null) return;
					var colliderGroupObj = m_VRMInstance.FindProperty($"{m_spring}.ColliderGroups.Array.data[{i}]").objectReferenceValue;
					ve.Q<TextField>().BindProperty(GetOrCreateSerializedObject(colliderGroupObj).FindProperty("Name"));
					var list = ve.Q<ListView>();
					list.BindProperty(GetOrCreateSerializedObject(colliderGroupObj).FindProperty("Colliders"));
				};
				colliderGroupsList.itemsAdded += (o) => { };

				// --- 動的UI生成 ---
				RebuildDynamicFields(root.Q<VisualElement>("DynamicFieldsContainer"));

				// Center
				centerField.BindProperty(m_VRMInstance.FindProperty($"{m_spring}.Center"));
				centerField.RegisterValueChangedCallback(evt =>
				{
					foreach (var instance in m_instances)
					{
						if (instance.SpringIndex >= 0)
							m_VRM10Instance.SpringBone.Springs[instance.SpringIndex].Center = evt.newValue as Transform;
					}
				});
			}

			// DefaultInspector
			var defaultInspector = new Foldout() { text = "DefaultInspector", value = false };
			defaultInspector.Add(new IMGUIContainer(() => DrawDefaultInspector()));
			root.Add(defaultInspector);

			return root;
		}

		void RebuildDynamicFields(VisualElement container)
		{
			container.Clear();
			if (m_instance.Target != null && m_instance.Spring != null
				&& m_instance.Spring.Joints != null && m_instance.Spring.Joints.Count > 0)
			{
				m_refreshUI = JointFieldUIGenerator.GenerateUI(
					container,
					m_instance.Spring.Joints,
					m_instance,
					() => { UpdateJointRuntime(); SceneView.RepaintAll(); },
					m_instances.Length > 1 ? m_instances : null);
			}
		}

		void BuildBranchSelector(VisualElement container, List<List<Transform>> chains)
		{
			container.Clear();
			var header = new Label("チェーンを選択してください:");
			header.style.unityFontStyleAndWeight = FontStyle.Bold;
			header.style.marginBottom = 4;
			container.Add(header);

			// 共通プレフィックスの長さを計算（表示の簡略化用）
			int commonPrefix = 0;
			if (chains.Count > 1)
			{
				int minLen = chains.Min(c => c.Count);
				for (int i = 0; i < minLen; i++)
				{
					if (chains.All(c => c[i] == chains[0][i]))
						commonPrefix = i + 1;
					else break;
				}
			}

			foreach (var chain in chains)
			{
				// 分岐点以降を表示
				var displayParts = chain.Skip(commonPrefix).Select(t => t.name).ToArray();
				string label;
				if (displayParts.Length <= 3)
					label = string.Join(" → ", displayParts);
				else
					label = $"{displayParts[0]} → ... → {displayParts[displayParts.Length - 1]}";
				label += $" ({chain.Count} bones)";

				var toggle = new Toggle(label)
				{
					value = true,
					userData = chain
				};
				toggle.style.marginLeft = 8;
				container.Add(toggle);
			}
		}

		List<List<Transform>> GetSelectedChains(VisualElement container)
		{
			var selected = new List<List<Transform>>();
			foreach (var child in container.Children())
			{
				if (child is Toggle toggle && toggle.value && toggle.userData is List<Transform> chain)
					selected.Add(chain);
			}
			return selected;
		}

		void CheckDuplicateSpring(Transform rootTransform, HelpBox warningBox)
		{
			if (m_VRM10Instance == null || rootTransform == null)
			{
				warningBox.style.display = DisplayStyle.None;
				return;
			}

			foreach (var spring in m_VRM10Instance.SpringBone.Springs)
			{
				if (spring == m_instance.Spring) continue;
				if (spring.Joints != null && spring.Joints.Any(j => j != null && j.transform == rootTransform))
				{
					warningBox.text = $"このボーンは既に別のSpring「{spring.Name}」に登録されています。";
					warningBox.style.display = DisplayStyle.Flex;
					return;
				}
			}
			warningBox.style.display = DisplayStyle.None;
		}

		void Init()
		{
			m_instance = this.target as VRM10SpringBoneEx;
			if (m_instance == null) return;
			m_VRM10Instance = m_instance.GetComponentInParent<Vrm10Instance>();
			if (m_VRM10Instance == null) return;

			m_VRMInstance?.Dispose();
			m_VRMInstance = new SerializedObject(m_VRM10Instance);

			var springIndex = m_instance.SpringIndex;
			m_spring = springIndex >= 0
				? $"SpringBone.Springs.Array.data[{springIndex}]"
				: null;

			m_instances = this.targets.ToList().Select(o => o as VRM10SpringBoneEx).Where(o => o != null).ToArray();
			m_springs = m_instances.Select(o => o.SpringIndex >= 0
				? $"SpringBone.Springs.Array.data[{o.SpringIndex}]"
				: null).ToArray();
		}

		// Play中のJoint更新: SetJointLevel（0.131+）or ReconstructSpringBone（旧版）
		static MethodInfo s_setJointLevel;
		static MethodInfo s_reconstructSpringBone;
		static PropertyInfo s_blittableProperty;
		static PropertyInfo s_springBoneProperty; // Vrm10Runtime.SpringBone (0.131+)
		static bool s_runtimeMethodsCached;

		void UpdateJointRuntime()
		{
			if (!EditorApplication.isPlaying || m_VRM10Instance == null) return;

			var runtime = m_VRM10Instance.Runtime;
			if (runtime == null) return;

			if (!s_runtimeMethodsCached)
			{
				// IVrm10SpringBoneRuntime は VRM 0.131+ でのみ存在するため、リフレクションで取得
				var runtimeType = AppDomain.CurrentDomain.GetAssemblies()
					.SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
					.FirstOrDefault(t => t.Name == "IVrm10SpringBoneRuntime");
				if (runtimeType != null)
					s_setJointLevel = runtimeType.GetMethod("SetJointLevel");
				s_blittableProperty = typeof(VRM10SpringBoneJoint).GetProperty("Blittable");

				// Vrm10Runtime.SpringBone プロパティ（0.131+）
				var vrm10RuntimeType = runtime.GetType();
				s_springBoneProperty = vrm10RuntimeType.GetProperty("SpringBone");
				s_reconstructSpringBone = vrm10RuntimeType.GetMethod("ReconstructSpringBone");
				s_runtimeMethodsCached = true;
			}

			// 0.131+: Runtime.SpringBone.SetJointLevel(transform, blittable)
			if (s_setJointLevel != null && s_blittableProperty != null
				&& s_springBoneProperty != null)
			{
				var springBone = s_springBoneProperty.GetValue(runtime);
				foreach (var inst in m_instances)
				{
					if (inst?.Target == null) continue;
					var blittable = s_blittableProperty.GetValue(inst.Target);
					s_setJointLevel.Invoke(springBone, new[] { inst.Target.transform, blittable });
				}
			}
			// フォールバック: ReconstructSpringBone（全バージョン）
			else if (s_reconstructSpringBone != null)
			{
				s_reconstructSpringBone.Invoke(runtime, null);
			}
		}
	}
}
