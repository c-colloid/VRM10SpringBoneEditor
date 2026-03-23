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
	/// <summary>
	/// 枝分かれ時のチェーン生成方式。VRC PhysBone の MultiChildType に相当。
	/// </summary>
	public enum MultiChildType
	{
		/// <summary>分岐ボーンをスキップ（物理なし、リジッド追従）</summary>
		Ignore,
		/// <summary>1番目の子を幹に統合、残りは分岐点から開始</summary>
		First,
		/// <summary>分岐点で分割、全枝独立（中立）</summary>
		Average,
	}

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

		// チェーン末尾のJoint（チェーン固有のため、共有ルートを持つ枝分かれでもSpringを一意に特定できる）
		[SerializeField]
		VRM10SpringBoneJoint m_tailJoint;

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

		[SerializeField]
		MultiChildType m_multiChildType = MultiChildType.Ignore;
		public MultiChildType MultiChild {
			get => m_multiChildType;
			set => m_multiChildType = value;
		}

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
		/// 末尾Joint → 先頭Joint → Transform の優先順位で解決する。
		/// 共有ルートを持つ枝分かれでも、末尾Jointはチェーン固有のため正しく特定できる。
		/// </summary>
		void InitJointIndex()
		{
			var targetVRM = this.GetComponentInParent<Vrm10Instance>(true);
			if (targetVRM == null) return;
			m_vrm10 = targetVRM;

			Vrm10InstanceSpringBone.Spring resolved = null;

			// 1. 末尾Joint参照でSpringを解決（最も信頼性が高い - チェーン固有）
			if (m_tailJoint != null)
			{
				resolved = targetVRM.SpringBone.Springs.Find(o =>
					o.Joints.Count > 0 && o.Joints[o.Joints.Count - 1] == m_tailJoint);
			}

			// 2. 先頭Joint参照でSpringを解決（旧データ互換・枝分かれなしの場合）
			if (resolved == null && m_target != null)
			{
				resolved = targetVRM.SpringBone.Springs.Find(o =>
					o.Joints.Count > 0 && o.Joints[0] == m_target);
			}

			// 3. Target TransformでJointを復元して解決
			//    （Undo後にm_targetが破棄されていてもTransformは残る場合）
			if (resolved == null && m_targetTransform != null)
			{
				if (m_targetTransform.TryGetComponent<VRM10SpringBoneJoint>(out var joint))
				{
					m_target = joint;
					resolved = targetVRM.SpringBone.Springs.Find(o =>
						o.Joints.Count > 0 && o.Joints[0] == joint);
				}
			}

			m_spring = resolved;
			m_springIndex = resolved != null
				? targetVRM.SpringBone.Springs.IndexOf(resolved)
				: -1;

			if (resolved != null)
			{
				m_name = resolved.Name;
				// m_target/m_tailJointを最新のSpring内容で同期
				if (resolved.Joints.Count > 0 && resolved.Joints[0] != null)
				{
					m_target = resolved.Joints[0];
					m_targetTransform = m_target.transform;
				}
				m_tailJoint = resolved.Joints[resolved.Joints.Count - 1];
			}
		}

		public void DestroyImmediate()
		{
			if (m_vrm10 == null || m_spring == null) return;
			m_vrm10.SpringBone.Springs.Remove(m_spring);
		}

		private void OnDrawGizmosSelected()
		{
			if (m_target == null) return;

			bool isActive = (ActiveInstance == null || ActiveInstance == this);
			float alpha = isActive ? 1.0f : 0.2f;

			// m_spring を直接使用（共有ルートでも正しい Spring を描画）
			if (m_spring != null && m_spring.Joints.Count > 0)
			{
				SpringBoneGizmoDrawer.DrawSpringGizmos(m_spring, m_target, alpha);
				return;
			}

			if (isActive)
				SpringBoneGizmoDrawer.DrawOrphanJoint(m_target.transform);
		}

		/// <summary>
		/// 指定したrootTransformを起点にJointを生成しSpringに登録する（線形チェーン用）。
		/// 内部でCollectChainsを呼び、最初のチェーンを使用する。
		/// </summary>
		public void GenerateJoints(Transform root = null, string springName = null)
		{
			if (root == null) root = this.transform;
			var chains = CollectChains(root, m_multiChildType);
			if (chains.Count == 0) return;
			GenerateJoints(chains[0], springName);
		}

		/// <summary>
		/// 指定したチェーン（Transform列）にJointを生成しSpringに登録する。
		/// springNameを指定すると常に新規Springを作成する（枝分かれ生成用）。
		/// springNameを省略すると既存Springを再利用し、無ければ新規作成する。
		/// </summary>
		public void GenerateJoints(List<Transform> chain, string springName = null)
		{
			if (chain == null || chain.Count == 0) return;

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
				var name = !string.IsNullOrEmpty(m_name) ? m_name : chain[0].name;
				name = EnsureUniqueSpringName(name, vrm);
				m_spring = new Vrm10InstanceSpringBone.Spring(name);
				vrm.SpringBone.Springs.Add(m_spring);
				m_name = name;
			}
			// else: 既存Springを再利用（Regenerate時）

			// --- Joint生成 ---
			var joints = new List<VRM10SpringBoneJoint>();
			VRM10SpringBoneJoint prevJoint = null;

			foreach (var t in chain)
			{
				bool isNew = false;
				if (!t.TryGetComponent<VRM10SpringBoneJoint>(out var joint))
				{
					joint = Undo.AddComponent<VRM10SpringBoneJoint>(t.gameObject);
					isNew = true;
				}

				// 新規Jointのみ親のフィールドをコピー
				if (isNew && prevJoint != null)
				{
					var fields = typeof(VRM10SpringBoneJoint).GetFields(BindingFlags.Public | BindingFlags.Instance);
					foreach (var f in fields)
						f.SetValue(joint, f.GetValue(prevJoint));
				}

				joints.Add(joint);
				prevJoint = joint;
			}

			m_target = joints[0];
			m_targetTransform = chain[0];
			m_tailJoint = joints[joints.Count - 1];

			// Joints更新（null安全）
			SetSpringJoints(m_spring, joints);

			InitJointIndex();

			Undo.CollapseUndoOperations(undoGroup);
		}

		/// <summary>
		/// rootから全てのリーフまでの線形チェーンを再帰的に収集する。
		/// 枝分かれがある場合、分岐点を共有しつつ各リーフまでの完全なパスを返す。
		/// </summary>
		public static List<List<Transform>> CollectChains(Transform root)
		{
			var chains = new List<List<Transform>>();
			if (root == null) return chains;
			CollectChainsRecursive(root, new List<Transform>(), chains);
			return chains;
		}

		static void CollectChainsRecursive(Transform current, List<Transform> prefix, List<List<Transform>> chains)
		{
			prefix.Add(current);

			if (current.childCount == 0)
			{
				// リーフ: チェーンを確定
				chains.Add(new List<Transform>(prefix));
			}
			else if (current.childCount == 1)
			{
				// 単一の子: チェーンを継続
				CollectChainsRecursive(current.GetChild(0), prefix, chains);
			}
			else
			{
				// 枝分かれ: 各子で分岐（prefixをコピーして渡す）
				for (int i = 0; i < current.childCount; i++)
					CollectChainsRecursive(current.GetChild(i), new List<Transform>(prefix), chains);
			}
		}

		/// <summary>
		/// MultiChildType に応じたチェーン収集を行う。
		/// </summary>
		public static List<List<Transform>> CollectChains(Transform root, MultiChildType mode)
		{
			if (root == null) return new List<List<Transform>>();
			switch (mode)
			{
				case MultiChildType.Ignore:  return CollectChainsIgnore(root);
				case MultiChildType.First:   return CollectChainsFirst(root);
				case MultiChildType.Average: return CollectChainsAverage(root);
				default:                     return CollectChains(root);
			}
		}

		// ────────────────────────────────────────
		// Ignore: 分岐ボーンを物理チェーンからスキップ
		// ────────────────────────────────────────

		/// <summary>
		/// Ignore モード: 分岐点（childCount>1）は物理から除外する。
		/// 分岐点の手前までが幹チェーン、各子は分岐点をアンカー(joints[0])として開始する。
		/// </summary>
		static List<List<Transform>> CollectChainsIgnore(Transform root)
		{
			var chains = new List<List<Transform>>();
			var trunk = new List<Transform>();
			CollectChainsIgnoreRecursive(root, trunk, chains);
			return chains;
		}

		static void CollectChainsIgnoreRecursive(Transform current, List<Transform> trunk, List<List<Transform>> chains)
		{
			trunk.Add(current);

			if (current.childCount == 0)
			{
				// リーフ: 幹チェーンを確定
				if (trunk.Count >= 2)
					chains.Add(new List<Transform>(trunk));
			}
			else if (current.childCount == 1)
			{
				// 単一の子: 幹を継続
				CollectChainsIgnoreRecursive(current.GetChild(0), trunk, chains);
			}
			else
			{
				// 分岐点: current を幹から除外し、幹を確定
				// current は物理なし（どのSpringにも含まれない）
				trunk.RemoveAt(trunk.Count - 1);
				if (trunk.Count >= 2)
					chains.Add(new List<Transform>(trunk));

				// 各子を独立チェーンとして開始（current は含めない — Transform共有禁止）
				for (int i = 0; i < current.childCount; i++)
				{
					var branch = new List<Transform>();
					CollectChainsIgnoreRecursive(current.GetChild(i), branch, chains);
				}
			}
		}

		// ────────────────────────────────────────
		// First: 1番目の子を幹に統合
		// ────────────────────────────────────────

		/// <summary>
		/// First モード: 各分岐点でGetChild(0)を辿った最長パスがメインチェーン。
		/// 2番目以降の子は分岐点をアンカーとして個別チェーンを開始する。
		/// </summary>
		static List<List<Transform>> CollectChainsFirst(Transform root)
		{
			var chains = new List<List<Transform>>();
			CollectChainsFirstRecursive(root, new List<Transform>(), chains);
			return chains;
		}

		static void CollectChainsFirstRecursive(Transform current, List<Transform> chain, List<List<Transform>> chains)
		{
			chain.Add(current);

			if (current.childCount == 0)
			{
				// リーフ: チェーンを確定
				if (chain.Count >= 2)
					chains.Add(new List<Transform>(chain));
			}
			else if (current.childCount == 1)
			{
				// 単一の子: 継続
				CollectChainsFirstRecursive(current.GetChild(0), chain, chains);
			}
			else
			{
				// 分岐点: 1番目の子はチェーンを継続（current はメインチェーンに含まれる）
				CollectChainsFirstRecursive(current.GetChild(0), chain, chains);

				// 2番目以降の子は独立チェーンとして開始（current は含めない — Transform共有禁止）
				for (int i = 1; i < current.childCount; i++)
				{
					var branch = new List<Transform>();
					CollectChainsFirstRecursive(current.GetChild(i), branch, chains);
				}
			}
		}

		// ────────────────────────────────────────
		// Average: 分岐点で分割、全枝独立
		// ────────────────────────────────────────

		/// <summary>
		/// Average モード: 分岐点までの幹セグメント（分岐点を含む）を確定し、
		/// 各子は分岐点をアンカーとして個別チェーンを開始する。
		/// </summary>
		static List<List<Transform>> CollectChainsAverage(Transform root)
		{
			var chains = new List<List<Transform>>();
			CollectChainsAverageRecursive(root, new List<Transform>(), chains);
			return chains;
		}

		static void CollectChainsAverageRecursive(Transform current, List<Transform> segment, List<List<Transform>> chains)
		{
			segment.Add(current);

			if (current.childCount == 0)
			{
				// リーフ: セグメントを確定
				if (segment.Count >= 2)
					chains.Add(new List<Transform>(segment));
			}
			else if (current.childCount == 1)
			{
				// 単一の子: 継続
				CollectChainsAverageRecursive(current.GetChild(0), segment, chains);
			}
			else
			{
				// 分岐点: ここまでの幹セグメント（分岐点を含む）を確定
				// 分岐点は幹Springに含まれ、中立的な物理が適用される
				if (segment.Count >= 2)
					chains.Add(new List<Transform>(segment));

				// 各子は独立チェーンとして開始（current は含めない — Transform共有禁止）
				for (int i = 0; i < current.childCount; i++)
				{
					var branch = new List<Transform>();
					CollectChainsAverageRecursive(current.GetChild(i), branch, chains);
				}
			}
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
