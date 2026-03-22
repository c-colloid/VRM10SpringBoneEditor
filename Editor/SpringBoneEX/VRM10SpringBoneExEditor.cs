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

			foreach (var spring in m_VRM10Instance.SpringBone.Springs)
			{
				if (!spring.Joints.Contains(m_instance.Target)) continue;

				int jointIndex = spring.Joints.IndexOf(m_instance.Target);

				// ラベル
				SpringBoneGizmoDrawer.DrawLabel(
					spring.Name, m_instance.SpringIndex,
					m_instance.Target.name, jointIndex,
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
				break;
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
			var setupMessage = root.Q<HelpBox>("SetupMessage");
			var branchSelector = root.Q<VisualElement>("BranchSelector");
			var generateBranchesBtn = root.Q<Button>("GenerateBranchesButton");
			var regenerateBtn = root.Q<Button>("RegenerateButton");
			var duplicateWarning = root.Q<HelpBox>("DuplicateWarning");
			var dynamicFieldsContainer = root.Q<VisualElement>("DynamicFieldsContainer");

			rootBone.SetEnabled(m_instances.Length < 2);
			rootBone.SetValueWithoutNotify(m_instance.Target?.transform);

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
			}
			UpdateSetupVisibility();

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
					RebuildDynamicFields(dynamicFieldsContainer);
					return;
				}

				// 重複チェック
				CheckDuplicateSpring(newTransform, duplicateWarning);

				// 枝分かれ検出
				var branches = VRM10SpringBoneEx.DetectBranches(newTransform);
				if (branches.Count > 1)
				{
					// 枝分かれあり: 選択UIを表示
					BuildBranchSelector(branchSelector, branches);
					branchSelector.style.display = DisplayStyle.Flex;
					generateBranchesBtn.style.display = DisplayStyle.Flex;
					setupMessage.style.display = DisplayStyle.None;
					regenerateBtn.style.display = DisplayStyle.None;
				}
				else
				{
					// 枝分かれなし: 即座に生成
					m_instance.GenerateJoints(newTransform);
					EditorUtility.SetDirty(m_instance);
					EditorUtility.SetDirty(m_VRM10Instance);
					Init();
					// Inspector全体を再構築（Spring依存UIのバインディングを更新）
					ActiveEditorTracker.sharedTracker.ForceRebuild();
				}
			});

			// Generate Selected（枝分かれ生成）
			generateBranchesBtn.clicked += () =>
			{
				var selectedBranches = GetSelectedBranches(branchSelector);
				if (selectedBranches.Count == 0) return;

				Undo.IncrementCurrentGroup();
				int undoGroup = Undo.GetCurrentGroup();

				bool first = true;
				foreach (var branch in selectedBranches)
				{
					if (first)
					{
						// 最初のブランチは現在のSpringBoneExを使用
						// springName指定で新規Spring作成（ブランチ名で一意化）
						m_instance.GenerateJoints(branch, branch.name);
						first = false;
					}
					else
					{
						// 追加ブランチは同じGameObjectにSpringBoneExを追加
						// GenerateJoints内で新規Spring作成 → Init()のauto-discoveryは影響しない
						var newEx = Undo.AddComponent<VRM10SpringBoneEx>(m_instance.gameObject);
						newEx.GenerateJoints(branch, branch.name);
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

		void BuildBranchSelector(VisualElement container, List<Transform> branches)
		{
			container.Clear();
			var header = new Label("ブランチを選択してください:");
			header.style.unityFontStyleAndWeight = FontStyle.Bold;
			header.style.marginBottom = 4;
			container.Add(header);

			foreach (var branch in branches)
			{
				int chainLength = VRM10SpringBoneEx.CountChainLength(branch);
				var toggle = new Toggle($"{branch.name} ({chainLength} bones)")
				{
					value = true,
					name = branch.name,
					userData = branch
				};
				toggle.style.marginLeft = 8;
				container.Add(toggle);
			}
		}

		List<Transform> GetSelectedBranches(VisualElement container)
		{
			var selected = new List<Transform>();
			foreach (var child in container.Children())
			{
				if (child is Toggle toggle && toggle.value && toggle.userData is Transform t)
					selected.Add(t);
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
