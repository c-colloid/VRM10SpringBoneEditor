using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UniVRM10;
using System.Linq;
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
		
		[SerializeField]
		bool m_drawColliders,
			m_stiffnessForceCurveValidate,
			m_gravityPowerCurveValidate,
			m_gravityDirCurveValidate,
			m_dragForceCurveValidate,
			m_jointRadiusCurveValidate;
			
		public bool DrawColliders => m_drawColliders;
		public bool StiffnessForceCurveValidate { 
			get => m_stiffnessForceCurveValidate;
			set => m_stiffnessForceCurveValidate = value;
		}
		public bool GravityPowerCurveValidate {
			get => m_gravityPowerCurveValidate;
			set => m_gravityPowerCurveValidate = value;
		}
		public bool GravityDirCurveValidate {
			get => m_gravityDirCurveValidate;
			set => m_gravityDirCurveValidate = value;
		}
		public bool DragForceCurveValidate {
			get => m_dragForceCurveValidate;
			set => m_dragForceCurveValidate = value;
		}
		public bool JointRadiusCurveValidate {
			get => m_jointRadiusCurveValidate;
			set => m_jointRadiusCurveValidate = value;
		}
		
		[SerializeField]
		float m_stiffnessForce,
			m_gravityPower,
			m_dragForce,
			m_jointRadius;
			
		[SerializeField]
		Vector3 m_gravityDir;
			
		// This function is called when the object is loaded.
		protected void OnEnable() {
			Init();
		}
		
		// This function is called when the MonoBehaviour will be destroyed.
		protected void OnDestroy() {
			//DestroyImmediate();
		}
			
		public void Init()
		{
			if (m_target == null)
			{
				var targetVRM = this.GetComponentInParent<Vrm10Instance>();
				m_vrm10 = targetVRM;
				var thisComponents = targetVRM.GetComponentsInChildren<VRM10SpringBoneEx>();
				if (m_spring != null && targetVRM.SpringBone.Springs.Select(o => o.Name).Contains(m_name))
				{
					m_spring = targetVRM.SpringBone.Springs.Where(o => o.Joints.Count <= 0 || o.Joints[0] == null).ToList().Find(o => o.Name == m_name);
					m_springIndex = targetVRM.SpringBone.Springs.IndexOf(m_spring);
					//m_name = m_spring?.Name;
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
				
				//m_spring = targetSpring;
				//m_springIndex = targetVRM.SpringBone.Springs.IndexOf(m_spring);
				//m_name = m_spring?.Name;
				
				m_target = 
					targetSpring
					//targetVRM.SpringBone.Springs[0]
					.Joints[0];
				m_targetTransform = m_target.transform;
						
				m_colliderGroups = targetSpring.ColliderGroups;
					
				Debug.Log("Init VRM10SpringBoneEx");
			}
			InitJointIndex();
			var targetJoint = m_target;
			m_drawColliders = targetJoint.m_drawCollider;
			m_stiffnessForce = targetJoint.m_stiffnessForce;
			m_gravityPower = targetJoint.m_gravityPower;
			m_gravityDir = targetJoint.m_gravityDir;
			m_dragForce = targetJoint.m_dragForce;
			m_jointRadius = targetJoint.m_jointRadius;
		}
		
		void InitJointIndex()
		{
			var targetVRM = this.GetComponentInParent<Vrm10Instance>(true);
			m_vrm10 = targetVRM;
			if (m_spring == null)
			{
				var thisComponents = targetVRM.GetComponentsInChildren<VRM10SpringBoneEx>();
				var targetSpring = targetVRM.SpringBone.Springs.Where(o => o.Joints.Count > 0).Where(o => thisComponents.All(ex => ex.Target != o.Joints[0])).FirstOrDefault();
				m_spring = targetSpring;
			}
			else if (string.IsNullOrEmpty(m_name))
			{
				m_spring = targetVRM.SpringBone.Springs.Find(o => o.Joints[0] == m_target);
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
			m_vrm10.SpringBone.Springs.RemoveAt(m_springIndex);
		}
		
		private void OnDrawGizmosSelected()
		{
			var vrm = GetComponentInParent<Vrm10Instance>();
			if (vrm != null)
			{
				var found = vrm.SpringBone.FindJoint(m_target);
				if (found.HasValue)
				{
					var (spring, i) = found.Value;
					// Spring の房全体を描画する
					spring.RequestDrawGizmos(m_drawColliders);
					return;
				}
			}

			// Spring から参照されていない孤立した Joint
			Gizmos.color = new Color(1, 0.75f, 0f);
			Gizmos.DrawSphere(transform.position, m_jointRadius);
		}
		
		public void CreateChildrenJoints()
		{
			var root = m_target;
			var joints = m_spring.Joints;
			joints.Clear();
			int i = 0;
			// 0
			joints.Insert(i,root);
			++i;
			// 1...
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
				// set params
				joint.m_dragForce = parent.m_dragForce;
				joint.m_gravityDir = parent.m_gravityDir;
				joint.m_gravityPower = parent.m_gravityPower;
				joint.m_jointRadius = parent.m_jointRadius;
				joint.m_stiffnessForce = parent.m_stiffnessForce;

				yield return joint;
				foreach (var x in MakeJointsRecursive(joint))
				{
					yield return x;
				}
			}
		}
		
		//public static T GetOrAddComponent<T>(this GameObject go) where T : Component
		//{
		//	if (go.TryGetComponent<T>(out var t))
		//	{
		//		return t;
		//	}
		//	return go.AddComponent<T>();
		//}
		
		public void Log(object Obj)
		{
			Debug.Log(Obj);
		}
	}
}
