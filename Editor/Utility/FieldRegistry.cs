using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace colloid.VRM10Ex.Utility
{
	/// <summary>
	/// Joint フィールドの UI 上書き定義。
	/// 型推論で十分な場合は登録不要。表示名・グループ・範囲のカスタマイズ時のみ使用。
	/// </summary>
	public class FieldOverride
	{
		public string FieldName;
		public string DisplayName;   // null = フィールド名から自動生成
		public string Group;         // null = メイン, "AngleLimit" = Foldout
		public float? SliderMin;     // null = [Range] or 0
		public float? SliderMax;     // null = [Range] or 1
		public int? Order;           // null = 発見順

		public FieldOverride(string fieldName) { FieldName = fieldName; }
	}

	/// <summary>
	/// VRM10SpringBoneJoint フィールドの UI 定義レジストリ。
	///
	/// 新 VRM バージョン対応:
	/// - 新フィールドは型推論で自動 UI 生成される（登録不要）
	/// - 表示名やグループを変えたい場合のみ Overrides に1行追加
	/// - フィールドが削除された場合は自動スキップ
	/// </summary>
	public static class FieldRegistry
	{
		public static readonly FieldOverride[] Overrides =
		{
			// メインパラメータ
			new("m_stiffnessForce") { DisplayName = "Stiffness Force", SliderMax = 4 },
			new("m_gravityPower")   { DisplayName = "Gravity Power", SliderMax = 2 },
			new("m_gravityDir")     { DisplayName = "Gravity Dir" },
			new("m_dragForce")      { DisplayName = "Drag Force" },
			new("m_jointRadius")    { DisplayName = "Joint Radius", SliderMax = 0.5f },

			// AngleLimit グループ
			new("m_anglelimitType")    { DisplayName = "Type", Group = "AngleLimit" },
			new("m_pitch")             { DisplayName = "Pitch", Group = "AngleLimit" },
			new("m_yaw")              { DisplayName = "Yaw", Group = "AngleLimit" },
			new("m_limitSpaceOffset")  { DisplayName = "Offset", Group = "AngleLimit" },
		};

		static readonly Dictionary<string, FieldOverride> s_lookup;

		static FieldRegistry()
		{
			s_lookup = new Dictionary<string, FieldOverride>();
			foreach (var o in Overrides)
				s_lookup[o.FieldName] = o;
		}

		public static FieldOverride GetOverride(string fieldName)
			=> s_lookup.TryGetValue(fieldName, out var o) ? o : null;

		/// <summary>
		/// フィールド名から表示名を自動生成。
		/// "m_stiffnessForce" → "Stiffness Force"
		/// </summary>
		public static string AutoDisplayName(string fieldName)
		{
			// "m_" プレフィックスを除去
			var name = fieldName.StartsWith("m_") ? fieldName.Substring(2) : fieldName;

			// camelCase → スペース区切り
			var result = new System.Text.StringBuilder();
			for (int i = 0; i < name.Length; i++)
			{
				if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
					result.Append(' ');
				result.Append(i == 0 ? char.ToUpper(name[i]) : name[i]);
			}
			return result.ToString();
		}

		/// <summary>
		/// 内部フィールド（UI に表示すべきでないもの）を判定
		/// </summary>
		public static bool IsInternalField(string fieldName)
		{
			// Unity 内部フィールドやスクリプト参照を除外
			return fieldName == "m_Script"
				|| fieldName == "m_ObjectHideFlags"
				|| fieldName == "m_Enabled"
				|| fieldName == "m_GameObject";
		}

		/// <summary>
		/// Joint 型に存在するがレジストリに未登録のフィールド一覧
		/// </summary>
		public static List<FieldInfo> GetUnregisteredFields(Type jointType)
		{
			return jointType
				.GetFields(BindingFlags.Public | BindingFlags.Instance)
				.Where(f => !IsInternalField(f.Name) && !s_lookup.ContainsKey(f.Name))
				.ToList();
		}

		/// <summary>
		/// [Range] 属性 or FieldOverride からスライダー範囲を取得
		/// </summary>
		public static (float min, float max) GetSliderRange(FieldInfo field, FieldOverride over)
		{
			var range = field.GetCustomAttribute<RangeAttribute>();
			float min = over?.SliderMin ?? range?.min ?? 0;
			float max = over?.SliderMax ?? range?.max ?? 1;
			return (min, max);
		}
	}
}
