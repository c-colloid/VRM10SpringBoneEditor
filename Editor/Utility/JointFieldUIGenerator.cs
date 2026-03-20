using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using UniVRM10;
using CustomUI;

namespace colloid.VRM10Ex.Utility
{
	/// <summary>
	/// VRM10SpringBoneJoint のフィールドを型推論で走査し、
	/// マルチJoint対応のUIを動的に生成する。
	/// </summary>
	public static class JointFieldUIGenerator
	{
		/// <summary>
		/// Joint の全フィールドを走査し、型推論でマルチJoint対応UIを生成。
		/// </summary>
		public static void GenerateUI(
			VisualElement container,
			List<VRM10SpringBoneJoint> allJoints,
			VRM10SpringBoneEx springBoneEx,
			Action onValueChanged)
		{
			if (allJoints == null || allJoints.Count == 0 || allJoints[0] == null) return;

			var jointType = typeof(VRM10SpringBoneJoint);
			var fields = jointType
				.GetFields(BindingFlags.Public | BindingFlags.Instance)
				.Where(f => !FieldRegistry.IsInternalField(f.Name))
				.ToList();

			// Overrides に登録されたフィールドを先に、順序通りに処理
			var orderedFields = new List<FieldInfo>();
			var registeredNames = new HashSet<string>();

			foreach (var over in FieldRegistry.Overrides)
			{
				var field = fields.Find(f => f.Name == over.FieldName);
				if (field != null)
				{
					orderedFields.Add(field);
					registeredNames.Add(field.Name);
				}
			}

			// 未登録フィールドを末尾に追加
			foreach (var field in fields)
			{
				if (!registeredNames.Contains(field.Name))
					orderedFields.Add(field);
			}

			string currentGroup = null;
			VisualElement groupContainer = container;
			var angleLimitFields = new Dictionary<string, VisualElement>();

			foreach (var fieldInfo in orderedFields)
			{
				var over = FieldRegistry.GetOverride(fieldInfo.Name);

				// グループ切替
				var group = over?.Group;
				if (group != currentGroup)
				{
					currentGroup = group;
					if (group != null)
					{
						groupContainer = CreateGroupFoldout(container, group);
						if (group == "AngleLimit")
						{
							// draft 警告
							groupContainer.Add(new HelpBox(
								"SpringBoneの角度制限はまだdraft仕様です。将来的に仕様が変更される可能性があります。",
								HelpBoxMessageType.Warning));
						}
					}
					else
					{
						groupContainer = container;
					}
				}

				var displayName = over?.DisplayName
					?? FieldRegistry.AutoDisplayName(fieldInfo.Name);

				// SerializedObject なしで型推論
				var element = CreateFieldUI(
					fieldInfo, displayName, over, allJoints,
					springBoneEx, onValueChanged);

				if (element != null)
				{
					groupContainer.Add(element);

					// AngleLimit グループ内のフィールドを追跡
					if (group == "AngleLimit")
						angleLimitFields[fieldInfo.Name] = element;
				}
			}

			// AngleLimit の条件表示ロジック
			SetupAngleLimitVisibility(allJoints, angleLimitFields, onValueChanged);
		}

		static VisualElement CreateFieldUI(
			FieldInfo fieldInfo, string displayName, FieldOverride over,
			List<VRM10SpringBoneJoint> joints, VRM10SpringBoneEx ex,
			Action onChanged)
		{
			var fieldType = fieldInfo.FieldType;

			if (fieldType == typeof(float))
				return CreateSliderWithCurve(fieldInfo, displayName, over, joints, ex, onChanged);

			if (fieldType == typeof(Vector3))
				return CreateVector3WithCurve(fieldInfo, displayName, joints, ex, onChanged);

			if (fieldType.IsEnum)
				return CreateEnumField(fieldInfo, displayName, joints, onChanged);

			if (fieldType == typeof(bool))
				return CreateToggle(fieldInfo, displayName, joints, onChanged);

			if (fieldType == typeof(int))
				return CreateIntField(fieldInfo, displayName, joints, onChanged);

			if (fieldType == typeof(Quaternion))
				return CreateQuaternionField(fieldInfo, displayName, joints, onChanged);

			return CreateDefaultField(fieldInfo, displayName, joints, onChanged);
		}

		// ─── float → SliderWithCurve ───
		static VisualElement CreateSliderWithCurve(
			FieldInfo field, string label, FieldOverride over,
			List<VRM10SpringBoneJoint> joints, VRM10SpringBoneEx ex,
			Action onChanged)
		{
			var (min, max) = FieldRegistry.GetSliderRange(field, over);
			var slider = new SliderWithCurve();
			slider.label = label;
			slider.lowValue = min;
			slider.highValue = max;
			slider.showInputField = true;

			// 初期値
			var currentValue = (float)field.GetValue(joints[0]);
			slider.SetValueWithoutNotify(currentValue);

			// カーブ初期状態の検出: Joint 間で値が異なればカーブモード
			var curveEnabled = joints.Select(j => field.GetValue(j)).Distinct().Count() > 1;
			ex.SetCurveEnabled(field.Name, curveEnabled);

			// SliderWithCurve 内部の CurveField と ToggleButton にアクセス
			var curveField = slider.Q<CurveField>();
			var toggleButton = slider.Q<ToggleButton>();

			// カーブ初期化
			InitializeCurve(curveField, field, joints, min, max);

			// カーブ表示状態
			if (curveField != null)
				curveField.style.display = curveEnabled ? DisplayStyle.Flex : DisplayStyle.None;
			if (toggleButton != null)
			{
				toggleButton.SetValueWithoutNotify(curveEnabled);
				toggleButton.text = curveEnabled ? "X" : "C";
			}

			// カーブトグル変更
			if (toggleButton != null)
			{
				toggleButton.RegisterValueChangedCallback<bool>(evt =>
				{
					ex.SetCurveEnabled(field.Name, evt.newValue);
				});
			}

			// カーブのコンテキストメニュー
			if (curveField != null)
			{
				curveField.AddManipulator(new ContextualMenuManipulator(evt =>
				{
					evt.menu.AppendAction("Copy",
						action => AnimationCurveUtility.Buffer = curveField.value,
						DropdownMenuAction.AlwaysEnabled);
					evt.menu.AppendAction("Paste",
						action => curveField.value = AnimationCurveUtility.Buffer,
						AnimationCurveUtility.Buffer == null
							? DropdownMenuAction.AlwaysDisabled
							: DropdownMenuAction.AlwaysEnabled);
				}));
			}

			// スライダー変更コールバック
			slider.RegisterValueChangedCallback(evt =>
			{
				if (joints.Count == 0) return;
				SetFieldWithUndo(joints[0], field, evt.newValue);

				if (curveField != null)
				{
					var curve = curveField.value;
					var add = curve.AddKey(0, evt.newValue);
					if (add < 0) curve.MoveKey(0, new Keyframe(0, evt.newValue));
					curveField.SetValueWithoutNotify(curve);
				}

				if (!ex.IsCurveEnabled(field.Name))
				{
					SetFieldWithUndo(joints, field, evt.newValue);
					curveField?.SetValueWithoutNotify(new AnimationCurve(
						new Keyframe { value = evt.newValue },
						new Keyframe { time = 1, value = evt.newValue }));
				}

				onChanged?.Invoke();
			});

			// カーブ変更コールバック
			if (curveField != null)
			{
				curveField.RegisterValueChangedCallback(evt =>
				{
					for (int i = 0; i < joints.Count; i++)
					{
						var t = (float)i / Math.Max(joints.Count - 1, 1);
						SetFieldWithUndo(joints[i], field, evt.newValue.Evaluate(t));
					}
					slider.SetValueWithoutNotify(evt.newValue.Evaluate(0));
					onChanged?.Invoke();
				});
			}

			return slider;
		}

		static void InitializeCurve(CurveField curveField, FieldInfo field,
			List<VRM10SpringBoneJoint> joints, float min, float max)
		{
			if (curveField == null || joints.Count == 0) return;
			var curve = new AnimationCurve();
			for (int i = 0; i < joints.Count; i++)
			{
				var val = (float)field.GetValue(joints[i]);
				curve.AddKey(i, val);
			}
			curveField.ranges = new Rect(0, min, 1, max);
			curveField.renderMode = CurveField.RenderMode.Mesh;
			curveField.value = AnimationCurveUtility.NormalizeCurveTime(curve);
		}

		// ─── Vector3 → Vector3Field + per-axis Curve ───
		static VisualElement CreateVector3WithCurve(
			FieldInfo field, string label,
			List<VRM10SpringBoneJoint> joints, VRM10SpringBoneEx ex,
			Action onChanged)
		{
			var groupBox = new GroupBox();
			groupBox.style.marginTop = 0;
			groupBox.style.marginBottom = 0;
			groupBox.style.marginLeft = 0;
			groupBox.style.marginRight = 0;
			groupBox.style.flexDirection = FlexDirection.Row;

			var nameLabel = new Label(label);
			nameLabel.style.minWidth = 120;
			groupBox.Add(nameLabel);

			var currentValue = (Vector3)field.GetValue(joints[0]);
			var vector3Field = new Vector3Field();
			vector3Field.SetValueWithoutNotify(currentValue);
			vector3Field.style.flexDirection = FlexDirection.Column;
			vector3Field.style.flexGrow = 1;

			// カーブボックス
			var curveBox = new GroupBox();
			curveBox.style.flexDirection = FlexDirection.Row;
			curveBox.style.marginTop = 0;
			curveBox.style.marginBottom = 0;
			curveBox.style.marginLeft = 0;
			curveBox.style.marginRight = 0;

			var curveEnabled = joints.Select(j => field.GetValue(j)).Distinct().Count() > 1;
			ex.SetCurveEnabled(field.Name, curveEnabled);
			curveBox.style.display = curveEnabled ? DisplayStyle.Flex : DisplayStyle.None;

			// 軸ごとの CurveField
			var curveFields = new CurveField[3];
			for (int axis = 0; axis < 3; axis++)
			{
				var cf = new CurveField();
				cf.style.flexGrow = 1;
				var axisCurve = new AnimationCurve();
				for (int i = 0; i < joints.Count; i++)
				{
					var v = (Vector3)field.GetValue(joints[i]);
					axisCurve.AddKey(i, v[axis]);
				}
				cf.ranges = new Rect(0, -1, 1, 2);
				cf.renderMode = CurveField.RenderMode.Mesh;
				cf.value = AnimationCurveUtility.NormalizeCurveTime(axisCurve);
				curveFields[axis] = cf;
				curveBox.Add(cf);
			}

			vector3Field.Add(curveBox);
			groupBox.Add(vector3Field);

			// トグルボタン
			var toggleButton = new ToggleButton();
			toggleButton.text = curveEnabled ? "X" : "C";
			toggleButton.SetValueWithoutNotify(curveEnabled);
			toggleButton.style.height = 18;
			toggleButton.style.marginRight = 1;
			toggleButton.style.marginLeft = 5;
			toggleButton.RegisterValueChangedCallback<bool>(evt =>
			{
				ex.SetCurveEnabled(field.Name, evt.newValue);
				curveBox.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
				toggleButton.text = evt.newValue ? "X" : "C";
			});
			groupBox.Add(toggleButton);

			// Vector3Field の値変更
			vector3Field.RegisterValueChangedCallback(evt =>
			{
				if (!ex.IsCurveEnabled(field.Name))
					SetFieldWithUndo(joints, field, evt.newValue);
				else
					SetFieldWithUndo(joints[0], field, evt.newValue);
				onChanged?.Invoke();
			});

			return groupBox;
		}

		// ─── enum → EnumField (全 Joint 同値適用) ───
		static VisualElement CreateEnumField(
			FieldInfo field, string label,
			List<VRM10SpringBoneJoint> joints, Action onChanged)
		{
			var currentValue = field.GetValue(joints[0]);
			var enumField = new EnumField(label, (Enum)currentValue);
			enumField.RegisterValueChangedCallback(evt =>
			{
				SetFieldWithUndo(joints, field, evt.newValue);
				onChanged?.Invoke();
			});
			return enumField;
		}

		// ─── bool → Toggle (全 Joint 同値適用) ───
		static VisualElement CreateToggle(
			FieldInfo field, string label,
			List<VRM10SpringBoneJoint> joints, Action onChanged)
		{
			var currentValue = (bool)field.GetValue(joints[0]);
			var toggle = new Toggle(label);
			toggle.SetValueWithoutNotify(currentValue);
			toggle.style.marginTop = 5;
			toggle.style.marginBottom = 5;
			toggle.RegisterValueChangedCallback(evt =>
			{
				SetFieldWithUndo(joints, field, evt.newValue);
				onChanged?.Invoke();
			});
			return toggle;
		}

		// ─── int → IntegerField (全 Joint 同値適用) ───
		static VisualElement CreateIntField(
			FieldInfo field, string label,
			List<VRM10SpringBoneJoint> joints, Action onChanged)
		{
			var currentValue = (int)field.GetValue(joints[0]);
			var intField = new IntegerField(label);
			intField.SetValueWithoutNotify(currentValue);
			intField.RegisterValueChangedCallback(evt =>
			{
				SetFieldWithUndo(joints, field, evt.newValue);
				onChanged?.Invoke();
			});
			return intField;
		}

		// ─── Quaternion → Vector4Field 表現 (全 Joint 同値適用) ───
		static VisualElement CreateQuaternionField(
			FieldInfo field, string label,
			List<VRM10SpringBoneJoint> joints, Action onChanged)
		{
			var q = (Quaternion)field.GetValue(joints[0]);
			var v4Field = new Vector4Field(label);
			v4Field.SetValueWithoutNotify(new Vector4(q.x, q.y, q.z, q.w));
			v4Field.RegisterValueChangedCallback(evt =>
			{
				var newQ = new Quaternion(evt.newValue.x, evt.newValue.y, evt.newValue.z, evt.newValue.w);
				SetFieldWithUndo(joints, field, newQ);
				onChanged?.Invoke();
			});
			return v4Field;
		}

		// ─── その他 → PropertyField フォールバック (全 Joint 同値適用) ───
		static VisualElement CreateDefaultField(
			FieldInfo field, string label,
			List<VRM10SpringBoneJoint> joints, Action onChanged)
		{
			// SerializedObject 経由で PropertyField を生成
			if (joints[0] == null) return null;
			var so = new SerializedObject(joints[0]);
			var prop = so.FindProperty(field.Name);
			if (prop == null) { so.Dispose(); return null; }

			var propField = new PropertyField(prop, label);
			propField.RegisterValueChangeCallback(evt =>
			{
				so.ApplyModifiedProperties();
				var val = field.GetValue(joints[0]);
				for (int i = 1; i < joints.Count; i++)
					SetFieldWithUndo(joints[i], field, val);
				onChanged?.Invoke();
			});
			propField.Bind(so);
			// UI要素が除去されたら SerializedObject を破棄
			propField.RegisterCallback<DetachFromPanelEvent>(evt => so.Dispose());
			return propField;
		}

		// ─── グループ Foldout 作成 ───
		static VisualElement CreateGroupFoldout(VisualElement parent, string groupName)
		{
			var foldout = new Foldout
			{
				text = $"{groupName} Settings",
				value = false
			};
			foldout.style.marginTop = 5;
			parent.Add(foldout);
			return foldout;
		}

		// ─── AngleLimit 条件表示 ───
		static void SetupAngleLimitVisibility(
			List<VRM10SpringBoneJoint> joints,
			Dictionary<string, VisualElement> angleLimitFields,
			Action onChanged)
		{
			if (!angleLimitFields.ContainsKey("m_anglelimitType")) return;

			var typeField = angleLimitFields["m_anglelimitType"] as EnumField;
			if (typeField == null) return;

			void UpdateVisibility()
			{
				var typeName = typeField.value?.ToString() ?? "None";
				bool isNone = typeName.Equals("None", StringComparison.OrdinalIgnoreCase);
				bool isSpherical = typeName.Equals("Spherical", StringComparison.OrdinalIgnoreCase);
				bool showPitch = !isNone;
				bool showYaw = isSpherical;
				bool showOffset = !isNone;

				if (angleLimitFields.TryGetValue("m_pitch", out var pitch))
					pitch.style.display = showPitch ? DisplayStyle.Flex : DisplayStyle.None;
				if (angleLimitFields.TryGetValue("m_yaw", out var yaw))
					yaw.style.display = showYaw ? DisplayStyle.Flex : DisplayStyle.None;
				if (angleLimitFields.TryGetValue("m_limitSpaceOffset", out var offset))
					offset.style.display = showOffset ? DisplayStyle.Flex : DisplayStyle.None;
			}

			typeField.RegisterValueChangedCallback(evt => UpdateVisibility());
			UpdateVisibility();
		}

		// ─── Undo / Dirty / Repaint ヘルパー ───

		/// <summary>
		/// Joint リストに対して Undo 記録 → フィールド値設定 → Dirty マーク を一括実行。
		/// </summary>
		static void SetFieldWithUndo(
			List<VRM10SpringBoneJoint> joints, FieldInfo field, object value, string undoName = "Change Joint Parameter")
		{
			foreach (var joint in joints)
			{
				if (joint == null) continue;
				Undo.RecordObject(joint, undoName);
				field.SetValue(joint, value);
				EditorUtility.SetDirty(joint);
			}
		}

		/// <summary>
		/// 単一 Joint に対して Undo 記録 → フィールド値設定 → Dirty マーク。
		/// </summary>
		static void SetFieldWithUndo(
			VRM10SpringBoneJoint joint, FieldInfo field, object value, string undoName = "Change Joint Parameter")
		{
			if (joint == null) return;
			Undo.RecordObject(joint, undoName);
			field.SetValue(joint, value);
			EditorUtility.SetDirty(joint);
		}
	}
}
