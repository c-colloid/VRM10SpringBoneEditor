using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UniVRM10;
using UniGLTF;
using colloid.VRM10Ex.Utility;

namespace colloid.VRM10Ex
{
	[AddComponentMenu("Scripts/UniVRM10/VRM10 Spring Bone Ex")]
	public class VRM10SpringBoneEx : MonoBehaviour
	{
		/// <summary>
		/// Inspectorで現在アクティブな（操作対象の）インスタンスを追跡。
		/// ギズモ描画でアクティブ/非アクティブの表示を切り替えるために使用。
		/// </summary>
		public static VRM10SpringBoneEx ActiveInstance { get; set; }

		Vrm10Instance m_vrm10;

		[SerializeField]
		VRM10SpringBoneJoint m_target;
		// Unity の破棄済みオブジェクト（fake null）を C# null に変換し、?.演算子を安全にする
		public VRM10SpringBoneJoint Target => m_target == null ? null : m_target;

		[SerializeField]
		Transform m_targetTransform;

		[SerializeField]
		Vrm10InstanceSpringBone.Spring m_spring;
		public Vrm10InstanceSpringBone.Spring Spring => m_spring;
		int m_springIndex = -1;
		public int SpringIndex => m_springIndex;

		[SerializeField]
		string m_name;
		public string Name {
			get => m_name;
			set => m_name = value;
		}

		[SerializeField]
		List<VRM10SpringBoneColliderGroup> m_colliderGroups = new List<VRM10SpringBoneColliderGroup>();

		// カーブが有効なフィールド名を動的管理
		[SerializeField]
		List<string> m_curveEnabledFields = new List<string>();

		public bool IsCurveEnabled(string fieldName) => m_curveEnabledFields.Contains(fieldName);
		public void SetCurveEnabled(string fieldName, bool enabled)
		{
			if (enabled && !m_curveEnabledFields.Contains(fieldName))
				m_curveEnabledFields.Add(fieldName);
			else if (!enabled)
				m_curveEnabledFields.Remove(fieldName);
		}

		protected void OnEnable()
		{
			Init();
		}

		protected void OnDestroy()
		{
		}

		/// <summary>
		/// 状態を初期化する。
		/// Target未設定の場合、VRM10Instance上の未割り当てSpringを自動取得する（後方互換性）。
		/// Springの自動作成は行わない（GenerateJointsで行う）。
		/// </summary>
		public void Init()
		{
			var targetVRM = this.GetComponentInParent<Vrm10Instance>();
			if (targetVRM == null) return;
			m_vrm10 = targetVRM;

			if (m_target == null && m_targetTransform == null)
			{
				// Target未設定: 既存の未割り当てSpringを自動取得（後方互換性）
				var thisComponents = targetVRM.GetComponentsInChildren<VRM10SpringBoneEx>();
				var unclaimedSpring = targetVRM.SpringBone.Springs
					.Where(o => o.Joints.Count > 0 && o.Joints[0] != null)
					.FirstOrDefault(o => thisComponents.All(ex => ex.Target != o.Joints[0]));

				if (unclaimedSpring != null)
				{
					m_target = unclaimedSpring.Joints[0];
					m_targetTransform = m_target.transform;
					m_colliderGroups = unclaimedSpring.ColliderGroups;
				}
			}
			InitJointIndex();
		}

		/// <summary>
		/// VRM10Instance上のSpringリストから、このコンポーネントに対応するSpringを解決する。
		/// Target Joint参照を主キーとし、名前ベースのフォールバックは行わない。
		/// </summary>
		void InitJointIndex()
		{
			var targetVRM = this.GetComponentInParent<Vrm10Instance>(true);
			if (targetVRM == null) return;
			m_vrm10 = targetVRM;

			m_spring = null;

			// 1. Target Joint参照でSpringを解決（最も信頼性が高い）
			if (m_target != null)
			{
				m_spring = targetVRM.SpringBone.Springs.Find(o =>
					o.Joints.Count > 0 && o.Joints[0] == m_target);
			}

			// 2. Target TransformでJointを復元して解決
			//    （Undo後にm_targetが破棄されていてもTransformは残る場合）
			if (m_spring == null && m_targetTransform != null)
			{
				if (m_targetTransform.TryGetComponent<VRM10SpringBoneJoint>(out var joint))
				{
					m_target = joint;
					m_spring = targetVRM.SpringBone.Springs.Find(o =>
						o.Joints.Count > 0 && o.Joints[0] == joint);
				}
			}

			m_springIndex = m_spring != null
				? targetVRM.SpringBone.Springs.IndexOf(m_spring)
				: -1;

			if (m_spring != null)
				m_name = m_spring.Name;
		}

		public void DestroyImmediate()
		{
			if (m_vrm10 == null || m_spring == null) return;
			m_vrm10.SpringBone.Springs.Remove(m_spring);
		}

		private void OnDrawGizmosSelected()
		{
			if (m_target == null) return;
			var vrm = GetComponentInParent<Vrm10Instance>();
			if (vrm == null) return;

			bool isActive = (ActiveInstance == null || ActiveInstance == this);
			float alpha = isActive ? 1.0f : 0.2f;

			foreach (var spring in vrm.SpringBone.Springs)
			{
				if (!spring.Joints.Contains(m_target)) continue;
				SpringBoneGizmoDrawer.DrawSpringGizmos(spring, m_target, alpha);
				return;
			}

			if (isActive)
				SpringBoneGizmoDrawer.DrawOrphanJoint(m_target.transform);
		}

		/// <summary>
		/// 指定したrootTransformを起点にJointを生成しSpringに登録する。
		/// springNameを指定すると常に新規Springを作成する（枝分かれ生成用）。
		/// springNameを省略すると既存Springを再利用し、無ければ新規作成する。
		/// </summary>
		public void GenerateJoints(Transform root = null, string springName = null)
		{
			if (root == null) root = this.transform;

			var vrm = GetComponentInParent<Vrm10Instance>();
			if (vrm == null) return;
			m_vrm10 = vrm;

			// Undoグループ化: 1回のCtrl+Zで完全に巻き戻す
			Undo.IncrementCurrentGroup();
			int undoGroup = Undo.GetCurrentGroup();

			Undo.RegisterCompleteObjectUndo(vrm, "Generate Spring Joints");
			Undo.RecordObject(this, "Generate Spring Joints");

			// --- Spring準備 ---
			if (!string.IsNullOrEmpty(springName))
			{
				// 名前指定: 常に新規Spring作成（枝分かれ生成用）
				springName = EnsureUniqueSpringName(springName, vrm);
				m_spring = new Vrm10InstanceSpringBone.Spring(springName);
				vrm.SpringBone.Springs.Add(m_spring);
				m_name = springName;
			}
			else if (m_spring == null || !vrm.SpringBone.Springs.Contains(m_spring))
			{
				// Springなし or VRM10Instanceに存在しない: 新規作成
				var name = !string.IsNullOrEmpty(m_name) ? m_name : root.name;
				name = EnsureUniqueSpringName(name, vrm);
				m_spring = new Vrm10InstanceSpringBone.Spring(name);
				vrm.SpringBone.Springs.Add(m_spring);
				m_name = name;
			}
			// else: 既存Springを再利用（Regenerate時）

			// --- Joint生成 ---
			if (!root.TryGetComponent<VRM10SpringBoneJoint>(out var rootJoint))
				rootJoint = Undo.AddComponent<VRM10SpringBoneJoint>(root.gameObject);

			m_target = rootJoint;
			m_targetTransform = root;

			// 子階層にJoint生成（最初の子のみ再帰）
			var joints = new List<VRM10SpringBoneJoint> { rootJoint };
			GenerateJointsRecursiveUndo(rootJoint, joints);

			// Joints更新（null安全）
			SetSpringJoints(m_spring, joints);

			InitJointIndex();

			Undo.CollapseUndoOperations(undoGroup);
		}

		/// <summary>
		/// 枝分かれを検出して子Transformリストを返す。
		/// childCount > 1 なら枝分かれあり。
		/// </summary>
		public static List<Transform> DetectBranches(Transform root)
		{
			var branches = new List<Transform>();
			if (root == null) return branches;
			for (int i = 0; i < root.childCount; i++)
				branches.Add(root.GetChild(i));
			return branches;
		}

		/// <summary>
		/// 指定Transformから最初の子のみを辿ったチェーンの長さを返す。
		/// </summary>
		public static int CountChainLength(Transform root)
		{
			int count = 0;
			var current = root;
			while (current != null)
			{
				count++;
				current = current.childCount > 0 ? current.GetChild(0) : null;
			}
			return count;
		}

		static void GenerateJointsRecursiveUndo(VRM10SpringBoneJoint parent, List<VRM10SpringBoneJoint> joints)
		{
			if (parent.transform.childCount == 0) return;

			var child = parent.transform.GetChild(0);
			if (!child.TryGetComponent<VRM10SpringBoneJoint>(out var joint))
				joint = Undo.AddComponent<VRM10SpringBoneJoint>(child.gameObject);

			// 親のフィールドをコピー
			var fields = typeof(VRM10SpringBoneJoint).GetFields(BindingFlags.Public | BindingFlags.Instance);
			foreach (var f in fields)
				f.SetValue(joint, f.GetValue(parent));

			joints.Add(joint);
			GenerateJointsRecursiveUndo(joint, joints);
		}

		/// <summary>
		/// SpringのJointsリストを安全に更新する。
		/// Jointsがnullの場合はリフレクションでフィールドを設定する。
		/// </summary>
		static void SetSpringJoints(Vrm10InstanceSpringBone.Spring spring, List<VRM10SpringBoneJoint> joints)
		{
			if (spring.Joints != null)
			{
				spring.Joints.Clear();
				spring.Joints.AddRange(joints);
			}
			else
			{
				// Spring.Joints未初期化 → リフレクションでフィールドを設定
				var field = typeof(Vrm10InstanceSpringBone.Spring).GetField("Joints");
				field?.SetValue(spring, new List<VRM10SpringBoneJoint>(joints));
			}
		}

		/// <summary>
		/// VRM10Instance内で一意なSpring名を生成する。
		/// 同名が存在する場合は「_1」「_2」...のサフィックスを付ける。
		/// </summary>
		public static string EnsureUniqueSpringName(string baseName, Vrm10Instance vrm)
		{
			var existingNames = new HashSet<string>(vrm.SpringBone.Springs.Select(s => s.Name));
			if (!existingNames.Contains(baseName)) return baseName;
			for (int i = 1; ; i++)
			{
				string candidate = $"{baseName}_{i}";
				if (!existingNames.Contains(candidate)) return candidate;
			}
		}

		public void CreateChildrenJoints()
		{
			var root = m_target;
			var joints = m_spring.Joints;
			joints.Clear();
			int i = 0;
			joints.Insert(i, root);
			++i;
			foreach (var joint in MakeJointsRecursive(root))
			{
				joints.Insert(i, joint);
				++i;
			}
		}

		static IEnumerable<VRM10SpringBoneJoint> MakeJointsRecursive(VRM10SpringBoneJoint parent)
		{
			if (parent.transform.childCount > 0)
			{
				var child = parent.transform.GetChild(0);
				var joint = child.gameObject.GetOrAddComponent<VRM10SpringBoneJoint>();

				// 全 public フィールドをリフレクションでコピー
				var fields = typeof(VRM10SpringBoneJoint).GetFields(BindingFlags.Public | BindingFlags.Instance);
				foreach (var f in fields)
					f.SetValue(joint, f.GetValue(parent));

				yield return joint;
				foreach (var x in MakeJointsRecursive(joint))
				{
					yield return x;
				}
			}
		}
	}
}
