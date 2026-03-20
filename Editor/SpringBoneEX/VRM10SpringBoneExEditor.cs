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
			m_refreshUI?.Invoke();
			SceneView.RepaintAll();
		}

		void OnSceneGUI()
		{
			if (m_instance == null || m_instance.Target == null) return;
			if (m_VRM10Instance == null) return;

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
					var head = spring.Joints[j]?.transform;
					var tail = spring.Joints[j + 1]?.transform;
					if (head == null || tail == null) continue;
					SpringBoneGizmoDrawer.DrawAngleLimitAndSpace(
						spring.Joints[j], head, tail);
				}
				break;
			}
		}

		public override VisualElement CreateInspectorGUI()
		{
			var root = new VisualElement();
			if (m_VRMInstance == null || m_spring == null)
			{
				root.Add(new HelpBox("VRM10 Instance が見つかりません。VRM ヒエラルキー内に配置してください。", HelpBoxMessageType.Error));
				return root;
			}
			m_springBoneExUI.CloneTree(root);

			// --- 静的UI バインディング ---

			// Spring Name
			var springName = root.Q<TextField>("SpringName");
			springName.BindProperty(m_VRMInstance.FindProperty($"{m_spring}.Name"));
			springName.RegisterValueChangedCallback(evt =>
			{
				m_instance.Name = evt.newValue;
			});

			// Target
			var targetBone = root.Q<ObjectField>("Target");
			targetBone.SetEnabled(m_instances.Length < 2);
			targetBone.RegisterCallback<DragUpdatedEvent>(evt =>
			{
				if (DragAndDrop.objectReferences.All(o => o is GameObject))
					DragAndDrop.visualMode = DragAndDropVisualMode.Generic;
			});
			targetBone.RegisterCallback<DragPerformEvent>(evt =>
			{
				DragAndDrop.AcceptDrag();
				var obj = DragAndDrop.objectReferences.FirstOrDefault() as GameObject;
				if (obj == null) return;
				if (!obj.TryGetComponent<VRM10SpringBoneJoint>(out var joint))
					joint = Undo.AddComponent<VRM10SpringBoneJoint>(obj);
				targetBone.value = joint;
			});
			targetBone.RegisterValueChangedCallback(evt =>
			{
				if (evt.previousValue != null)
					(evt.previousValue as VRM10SpringBoneJoint).GetComponentsInChildren<VRM10SpringBoneJoint>().ToList().ForEach(o => DestroyImmediate(o));

				var val = evt.newValue as VRM10SpringBoneJoint;
				if (val == null)
				{
					m_VRM10Instance.SpringBone.Springs[m_instance.SpringIndex].Joints.Clear();
					return;
				}
				if (val.GetComponentsInChildren<Transform>().Any(o => !o.TryGetComponent<VRM10SpringBoneJoint>(out var result)))
					val.GetComponentsInChildren<Transform>().Where(o => !o.TryGetComponent<VRM10SpringBoneJoint>(out var result)).ToList()
					.ForEach(o => o.gameObject.AddComponent<VRM10SpringBoneJoint>());

				List<VRM10SpringBoneJoint> joints =
					val.GetComponentsInChildren<VRM10SpringBoneJoint>().ToList();
				m_VRM10Instance.SpringBone.Springs[m_instance.SpringIndex].Joints = joints;
			});

			// ColliderGroups ListView
			var colliderGroupsList = root.Q<ListView>();
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
			var container = root.Q<VisualElement>("DynamicFieldsContainer");
			if (m_instance.Target != null && m_instance.Spring != null && m_instance.Spring.Joints.Count > 0)
			{
				foreach (var instance in m_instances)
				{
					m_refreshUI = JointFieldUIGenerator.GenerateUI(
						container,
						instance.Spring.Joints,
						instance,
						() => { UpdateJointRuntime(); SceneView.RepaintAll(); });
				}
			}

			// Center
			root.Q<ObjectField>("Center").BindProperty(m_VRMInstance.FindProperty($"{m_spring}.Center"));
			root.Q<ObjectField>("Center").RegisterValueChangedCallback(evt =>
			{
				foreach (var instance in m_instances)
				{
					m_VRM10Instance.SpringBone.Springs[instance.SpringIndex].Center = evt.newValue as Transform;
				}
			});

			// DefaultInspector
			var defaultInspector = new Foldout() { text = "DefaultInspector", value = false };
			defaultInspector.Add(new IMGUIContainer(() => DrawDefaultInspector()));
			root.Add(defaultInspector);

			return root;
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
				&& s_springBoneProperty != null && m_instance.Target != null)
			{
				var springBone = s_springBoneProperty.GetValue(runtime);
				var blittable = s_blittableProperty.GetValue(m_instance.Target);
				s_setJointLevel.Invoke(springBone, new[] { m_instance.Target.transform, blittable });
			}
			// フォールバック: ReconstructSpringBone（全バージョン）
			else if (s_reconstructSpringBone != null)
			{
				s_reconstructSpringBone.Invoke(runtime, null);
			}
		}
	}
}
