using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UniVRM10;
using UniGLTF;
using colloid.VRM10Ex.Utility;

namespace colloid.VRM10Ex
{
	[AddComponentMenu("Scripts/UniVRM10/VRM10 Spring Bone Ex")]
	public class VRM10SpringBoneEx : MonoBehaviour
	{
		Vrm10Instance m_vrm10;

		[SerializeField]
		VRM10SpringBoneJoint m_target;
		public VRM10SpringBoneJoint Target => m_target;

		[SerializeField]
		Transform m_targetTransform;

		[SerializeField]
		Vrm10InstanceSpringBone.Spring m_spring;
		public Vrm10InstanceSpringBone.Spring Spring => m_spring;
		int m_springIndex;
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

		public void Init()
		{
			if (m_target == null)
			{
				var targetVRM = this.GetComponentInParent<Vrm10Instance>();
				if (targetVRM == null) return;
				m_vrm10 = targetVRM;
				var thisComponents = targetVRM.GetComponentsInChildren<VRM10SpringBoneEx>();
				if (m_spring != null && targetVRM.SpringBone.Springs.Select(o => o.Name).Contains(m_name))
				{
					m_spring = targetVRM.SpringBone.Springs.Where(o => o.Joints.Count <= 0 || o.Joints[0] == null).ToList().Find(o => o.Name == m_name);
					if (m_spring == null) return;
					m_springIndex = targetVRM.SpringBone.Springs.IndexOf(m_spring);
					return;
				}

				if (!targetVRM.SpringBone.Springs.Where(o => o.Joints.Count > 0).Any(o => thisComponents.All(ex => ex.Target != o.Joints[0])))
				{
					m_spring = new Vrm10InstanceSpringBone.Spring(this.gameObject.name);
					targetVRM.SpringBone.Springs.Add(m_spring);
					m_springIndex = targetVRM.SpringBone.Springs.IndexOf(m_spring);
					m_name = m_spring?.Name;
					return;
				}

				var targetSpring = targetVRM.SpringBone.Springs.Where(o => o.Joints.Count > 0).Where(o => thisComponents.All(ex => ex.Target != o.Joints[0])).FirstOrDefault();
				if (targetSpring == null) return;

				m_target = targetSpring.Joints[0];
				m_targetTransform = m_target.transform;
				m_colliderGroups = targetSpring.ColliderGroups;
			}
			InitJointIndex();
		}

		void InitJointIndex()
		{
			var targetVRM = this.GetComponentInParent<Vrm10Instance>(true);
			if (targetVRM == null) return;
			m_vrm10 = targetVRM;
			if (m_spring == null)
			{
				var thisComponents = targetVRM.GetComponentsInChildren<VRM10SpringBoneEx>();
				var targetSpring = targetVRM.SpringBone.Springs.Where(o => o.Joints.Count > 0).Where(o => thisComponents.All(ex => ex.Target != o.Joints[0])).FirstOrDefault();
				m_spring = targetSpring;
			}
			else if (string.IsNullOrEmpty(m_name))
			{
				m_spring = targetVRM.SpringBone.Springs.Find(o => o.Joints.Count > 0 && o.Joints[0] == m_target);
				m_name = m_spring?.Name;
			}
			else
			{
				m_spring = targetVRM.SpringBone.Springs.Find(o => o.Name == m_name);
			}
			m_springIndex = targetVRM.SpringBone.Springs.IndexOf(m_spring);
		}

		public void DestroyImmediate()
		{
			if (m_vrm10 == null || m_springIndex < 0 || m_springIndex >= m_vrm10.SpringBone.Springs.Count) return;
			m_vrm10.SpringBone.Springs.RemoveAt(m_springIndex);
		}

		private void OnDrawGizmosSelected()
		{
			if (m_target == null) return;
			var vrm = GetComponentInParent<Vrm10Instance>();
			if (vrm == null) return;

			foreach (var spring in vrm.SpringBone.Springs)
			{
				if (!spring.Joints.Contains(m_target)) continue;
				SpringBoneGizmoDrawer.DrawSpringGizmos(spring, m_target);
				return;
			}

			SpringBoneGizmoDrawer.DrawOrphanJoint(m_target.transform);
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
