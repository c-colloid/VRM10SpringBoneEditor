using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UniVRM10;

namespace colloid.VRM10Ex.Utility
{
	/// <summary>
	/// SpringBone のギズモ描画を一元管理。
	/// - チェーンライン・ワイヤースフィア（Gizmos API — OnDrawGizmosSelected 用）
	/// - コライダー（公式 API: VRM10SpringBoneCollider.DrawGizmos()）
	/// - AngleLimit / Space（公式 private からポート — Handles API — OnSceneGUI 用）
	/// - ラベル（Handles API）
	/// </summary>
	public static class SpringBoneGizmoDrawer
	{
		// ============================================================
		//  Gizmos API（OnDrawGizmosSelected から呼ぶ）
		// ============================================================

		/// <summary>
		/// Spring 全体のギズモを描画（チェーンライン + ワイヤースフィア + コライダー）
		/// alpha: 1.0=フルカラー（アクティブ）, 0.2=半透明（非アクティブ）
		/// </summary>
		public static void DrawSpringGizmos(
			Vrm10InstanceSpringBone.Spring spring,
			VRM10SpringBoneJoint target,
			float alpha = 1.0f)
		{
			if (alpha >= 1.0f)
				DrawChainLines(spring);

			// ルート Joint（固定サイズ 0.01f — VRM10SpringBone 公式と同じ）
			var backup = Gizmos.matrix;
			Gizmos.color = new Color(0, 1, 0, alpha);
			Gizmos.DrawSphere(target.transform.position, 0.01f);

			// 各 Joint のワイヤースフィア（m_jointRadius）
			for (int k = 0; k < spring.Joints.Count - 1; k++)
			{
				var currentJoint = spring.Joints[k];
				var nextJoint = spring.Joints[k + 1];
				if (currentJoint == null || nextJoint == null) continue;
				var c = (currentJoint == target) ? Color.green : Color.yellow;
				Gizmos.color = new Color(c.r, c.g, c.b, alpha);
				Gizmos.matrix = Matrix4x4.TRS(
					nextJoint.transform.position,
					nextJoint.transform.rotation,
					Vector3.one);
				Gizmos.DrawWireSphere(Vector3.zero, currentJoint.m_jointRadius);
			}
			Gizmos.matrix = backup;

			if (alpha >= 1.0f)
				DrawColliderGroups(spring.ColliderGroups);
		}

		/// <summary>孤立 Joint（Spring に所属しない）の描画</summary>
		public static void DrawOrphanJoint(Transform target)
		{
			Gizmos.color = new Color(1, 0.75f, 0f);
			Gizmos.DrawSphere(target.position, 0.02f);
		}

		// --- チェーンライン: Spring.DrawGizmos() をリフレクションで呼び分け ---
		static MethodInfo s_drawGizmos;
		static MethodInfo s_requestDrawGizmos;
		static bool s_chainMethodsCached;

		static void DrawChainLines(Vrm10InstanceSpringBone.Spring spring)
		{
			if (!s_chainMethodsCached)
			{
				var springType = typeof(Vrm10InstanceSpringBone.Spring);
				s_drawGizmos = springType.GetMethod("DrawGizmos", Type.EmptyTypes);
				s_requestDrawGizmos = springType.GetMethod("RequestDrawGizmos");
				s_chainMethodsCached = true;
			}

			if (s_drawGizmos != null)
				s_drawGizmos.Invoke(spring, null);
			else if (s_requestDrawGizmos != null)
				s_requestDrawGizmos.Invoke(spring, new object[] { false });
		}

		// --- コライダー: 公式 public API ---
		static void DrawColliderGroups(List<VRM10SpringBoneColliderGroup> groups)
		{
			if (groups == null) return;
			foreach (var group in groups)
			{
				if (group == null) continue;
				foreach (var collider in group.Colliders)
					if (collider != null) collider.DrawGizmos();
			}
		}

		// ============================================================
		//  Handles API（OnSceneGUI から呼ぶ）
		// ============================================================

		// --- リフレクションキャッシュ（AngleLimit フィールド — VRM 0.131+ のみ）---
		static FieldInfo s_angleLimitType;
		static FieldInfo s_pitch;
		static FieldInfo s_yaw;
		static FieldInfo s_limitSpaceOffset;
		static bool s_angleLimitCached;
		static bool s_angleLimitAvailable;

		static void CacheAngleLimitFields()
		{
			if (s_angleLimitCached) return;
			s_angleLimitCached = true;

			var t = typeof(VRM10SpringBoneJoint);
			s_angleLimitType = t.GetField("m_anglelimitType", BindingFlags.Public | BindingFlags.Instance);
			s_pitch = t.GetField("m_pitch", BindingFlags.Public | BindingFlags.Instance);
			s_yaw = t.GetField("m_yaw", BindingFlags.Public | BindingFlags.Instance);
			s_limitSpaceOffset = t.GetField("m_limitSpaceOffset", BindingFlags.Public | BindingFlags.Instance);

			s_angleLimitAvailable = s_angleLimitType != null && s_pitch != null
				&& s_yaw != null && s_limitSpaceOffset != null;
		}

		/// <summary>
		/// AngleLimit + Space の描画（Handles API — OnSceneGUI から呼ぶ）。
		/// VRM 0.131 未満では自動スキップ。
		/// </summary>
		public static void DrawAngleLimitAndSpace(
			VRM10SpringBoneJoint joint, Transform head, Transform tail)
		{
			CacheAngleLimitFields();
			if (!s_angleLimitAvailable) return;

			var backupMatrix = Handles.matrix;
			try
			{
				var localAxis = head.worldToLocalMatrix.MultiplyPoint(tail.position);
				var limitTailPos = Vector3.up * localAxis.magnitude;
				var limitRotation = CalcLimitSpace(head.rotation, localAxis);

				// limitSpaceOffset 適用（null の場合は identity）
				var rawOffset = s_limitSpaceOffset.GetValue(joint);
				var offset = rawOffset != null ? (Quaternion)rawOffset : Quaternion.identity;
				limitRotation *= offset;
				limitRotation.Normalize();

				var limitSpace = Matrix4x4.TRS(head.position, limitRotation, Vector3.one);
				DrawSpace(limitSpace, limitTailPos.magnitude);

				var typeName = s_angleLimitType.GetValue(joint)?.ToString() ?? "None";
				if (typeName == "None") return;

				var pitch = (float)s_pitch.GetValue(joint);
				var yaw = (float)s_yaw.GetValue(joint);

				Handles.matrix = limitSpace;
			switch (typeName)
			{
				case "Cone":
					DrawCone(limitTailPos, pitch);
					break;
				case "Hinge":
					DrawHinge(limitTailPos, pitch, Color.cyan);
					break;
				case "Spherical":
					DrawHinge(limitTailPos, pitch, Color.cyan * 0.5f);
					DrawSpherical(limitTailPos, pitch, yaw);
					break;
				}
			}
			finally
			{
				Handles.matrix = backupMatrix;
			}
		}

		/// <summary>ラベル描画（Handles API）</summary>
		public static void DrawLabel(
			string springName, int springIndex,
			string jointName, int jointIndex,
			Vector3 position)
		{
			var label = string.IsNullOrEmpty(springName)
				? $"[{springIndex}][{jointIndex}]{jointName}"
				: $"[{springIndex}]{springName}[{jointIndex}]{jointName}";
			Handles.Label(position, label);
		}

		// ============================================================
		//  内部ヘルパー（VRM10SpringBoneJointEditor からポート）
		// ============================================================

		static Quaternion CalcLimitSpace(Quaternion headRotation, Vector3 boneAxis)
		{
			var jointLocalAxisSpace = Quaternion.FromToRotation(Vector3.up, boneAxis);
			return headRotation * jointLocalAxisSpace;
		}

		static void DrawSpace(Matrix4x4 limitSpace, float size)
		{
			float half = size * 0.5f;
			Handles.matrix = limitSpace;
			Handles.color = Color.red;
			var x = Vector3.right * half;
			Handles.DrawLine(x, -x);
			Handles.color = Color.green;
			Handles.DrawLine(Vector3.zero, Vector3.up * size);
			Handles.color = Color.blue;
			var z = Vector3.forward * half;
			Handles.DrawLine(-z, z);

			Handles.color = new Color(1, 1, 1, 0.1f);
			Handles.DrawSolidDisc(Vector3.zero, Vector3.up, half);
		}

		static void DrawCone(in Vector3 limitTailPos, float pitch)
		{
			var s = Mathf.Sin(pitch);
			var c = Mathf.Cos(pitch);

			Handles.color = Color.cyan;
			var r = Mathf.Tan(pitch) * limitTailPos.magnitude * c;
			Handles.DrawWireDisc(limitTailPos * c, Vector3.up, r, 1);

			var pz = new Vector3(0, c, s) * limitTailPos.magnitude;
			var nz = new Vector3(0, c, -s) * limitTailPos.magnitude;
			var px = new Vector3(s, c, 0) * limitTailPos.magnitude;
			var nx = new Vector3(-s, c, 0) * limitTailPos.magnitude;
			Handles.DrawLine(Vector3.zero, pz);
			Handles.DrawLine(Vector3.zero, nz);
			Handles.DrawLine(Vector3.zero, px);
			Handles.DrawLine(Vector3.zero, nx);

			Handles.color = new Color(0, 1, 1, 0.1f);
			Handles.Label(Vector3.Slerp(limitTailPos, pz, 0.5f) * 0.5f,
				$"pitch: {pitch * Mathf.Rad2Deg:F0}°");
			Handles.DrawSolidArc(Vector3.zero, Vector3.Cross(limitTailPos, pz),
				limitTailPos,
				pitch * Mathf.Rad2Deg,
				limitTailPos.magnitude * 0.5f);
		}

		static void DrawHinge(in Vector3 limitTailPos, float pitch, Color color)
		{
			var s = Mathf.Sin(pitch);
			var c = Mathf.Cos(pitch);

			var a = new Vector3(0, c, s) * limitTailPos.magnitude;
			var b = new Vector3(0, c, -s) * limitTailPos.magnitude;
			Handles.color = color;
			Handles.DrawLine(Vector3.zero, a);
			Handles.DrawLine(Vector3.zero, b);

			Handles.DrawWireArc(Vector3.zero, Vector3.left,
				new Vector3(0, c, s),
				pitch * 2 * Mathf.Rad2Deg,
				limitTailPos.magnitude);

			color.a = 0.1f;
			Handles.color = color;
			Handles.Label(Vector3.Slerp(limitTailPos, a, 0.5f) * 0.5f,
				$"pitch: {pitch * Mathf.Rad2Deg:F0}°");
			Handles.DrawSolidArc(Vector3.zero, Vector3.left,
				new Vector3(0, c, s),
				pitch * Mathf.Rad2Deg,
				limitTailPos.magnitude * 0.5f);
		}

		static void DrawSpherical(in Vector3 limitTailPos, float pitch, float yaw)
		{
			Handles.color = Color.cyan;

			var ts = Mathf.Sin(pitch);
			var tc = Mathf.Cos(pitch);
			var ps = Mathf.Sin(yaw);
			var pc = Mathf.Cos(yaw);

			var x = ps;
			var y = pc * tc;
			var z = pc * ts;

			var a = new Vector3(x, y, z);
			var b = new Vector3(-x, y, z);
			var c = new Vector3(-x, y, -z);
			var d = new Vector3(x, y, -z);

			Handles.DrawLine(Vector3.zero, a * limitTailPos.magnitude);
			Handles.DrawLine(Vector3.zero, b * limitTailPos.magnitude);
			Handles.DrawLine(Vector3.zero, c * limitTailPos.magnitude);
			Handles.DrawLine(Vector3.zero, d * limitTailPos.magnitude);

			// ab / cd
			Handles.DrawWireArc(Vector3.zero, Vector3.Cross(a, b).normalized,
				a * limitTailPos.magnitude,
				Vector3.Angle(a, b),
				limitTailPos.magnitude);
			Handles.DrawWireArc(Vector3.zero, Vector3.Cross(c, d).normalized,
				c * limitTailPos.magnitude,
				Vector3.Angle(c, d),
				limitTailPos.magnitude);
			Handles.Label(Vector3.Slerp(a, b, 0.25f) * limitTailPos.magnitude,
				$"yaw: {yaw * Mathf.Rad2Deg:F0}°");

			// bc / da
			Handles.DrawWireArc(Vector3.zero, Vector3.Cross(b, c).normalized,
				b * limitTailPos.magnitude,
				Vector3.Angle(b, c),
				limitTailPos.magnitude);
			Handles.DrawWireArc(Vector3.zero, Vector3.Cross(d, a).normalized,
				d * limitTailPos.magnitude,
				Vector3.Angle(d, a),
				limitTailPos.magnitude);
		}
	}
}
