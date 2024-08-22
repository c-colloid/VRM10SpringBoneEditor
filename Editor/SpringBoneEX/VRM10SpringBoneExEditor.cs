using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using UniVRM10;
using System.Linq;
using System;
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
		
		//MultipleEdit
		VRM10SpringBoneEx[] m_instances;
		string[] m_springs;
		
		float m_stiffnessSlider,
			m_gravitySlider,
			m_dragSlider,
			m_radiusSlider;
		
		// This function is called when the object is loaded.
		protected void OnEnable() {
			if (m_springBoneExUI == null) 
				m_springBoneExUI = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AssetDatabase.GUIDToAssetPath("513d600b93ba1304e8347be91dd549e0"));
			if (m_springBoneExColliderGroupsList == null) 
				m_springBoneExColliderGroupsList = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AssetDatabase.GUIDToAssetPath("e1ce3a43555952b47b50c0fe89b56c37"));
			(this.target as VRM10SpringBoneEx).Init();
			Init();
		}
		
		// This function is called when the scriptable object will be destroyed.
		protected void OnDestroy() {
			//(this.target as VRM10SpringBoneEx).DestroyImmediate();
			if (this.target != null) return;
			
			m_instance.DestroyImmediate();
		}
		
		public override VisualElement CreateInspectorGUI()
		{
			var root = new VisualElement();
			m_springBoneExUI.CloneTree(root);
			
			var SpringName = root.Q<TextField>("SpringName");
			SpringName.BindProperty(m_VRMInstance.FindProperty($"{m_spring}.Name"));
			SpringName.RegisterValueChangedCallback(evt => {
				//m_VRM10Instance.SpringBone.Springs[m_instance.SpringIndex].Name = evt.newValue;
				m_instance.Name = evt.newValue;
			});
			
			var TargetBone = root.Q<ObjectField>("Target");
			TargetBone.SetEnabled(m_instances.Length < 2);
			TargetBone.RegisterCallback<DragUpdatedEvent>(evt => {
				if (DragAndDrop.objectReferences.All(o => o is GameObject))
					DragAndDrop.visualMode = DragAndDropVisualMode.Generic;
			});
			TargetBone.RegisterCallback<DragPerformEvent>(evt => {
				var obj = DragAndDrop.objectReferences.FirstOrDefault();
				if (!(obj as GameObject).TryGetComponent<VRM10SpringBoneJoint>(out var result))
					obj = (obj as GameObject).AddComponent<VRM10SpringBoneJoint>();
				TargetBone.value = obj as VRM10SpringBoneJoint;
			});
			TargetBone.RegisterValueChangedCallback(evt => {
				if (evt.previousValue != null)
					(evt.previousValue as VRM10SpringBoneJoint).GetComponentsInChildren<VRM10SpringBoneJoint>().ToList().ForEach(o => DestroyImmediate(o));
				
				var val = evt.newValue as VRM10SpringBoneJoint;
				if (val == null){
					m_VRM10Instance.SpringBone.Springs[m_instance.SpringIndex].Joints.Clear();
					return;
				}
				if (val.GetComponentsInChildren<Transform>().Any(o => !o.TryGetComponent<VRM10SpringBoneJoint>(out var result)))
					val.GetComponentsInChildren<Transform>().Where(o => !o.TryGetComponent<VRM10SpringBoneJoint>(out var result)).ToList()
					.ForEach(o => o.gameObject.AddComponent<VRM10SpringBoneJoint>());
					
				List<VRM10SpringBoneJoint> joints = 
					val.GetComponentsInChildren<VRM10SpringBoneJoint>().ToList();
				m_VRM10Instance.SpringBone.Springs[m_instance.SpringIndex].Joints = joints;
				//m_instance.Target = joints[0];
			});
			
			var SpringBoneExColliderGroupsList = root.Q<ListView>();
			SpringBoneExColliderGroupsList.BindProperty(m_VRMInstance.FindProperty($"{m_spring}.ColliderGroups"));
			SpringBoneExColliderGroupsList.makeItem = () => {
				var ve = 
					m_springBoneExColliderGroupsList.CloneTree();
				ve.Q<ObjectField>().RegisterValueChangedCallback(evt => {
					if (evt.newValue == null) {
						ve.Q<TextField>().Unbind();
						ve.Q<ListView>().Unbind();
						ve.Q<ListView>().makeItem = () => new ObjectField(){objectType = typeof(VRM10SpringBoneCollider)};
						return;
					}
					
					ve.Q<TextField>().BindProperty(new SerializedObject(evt.newValue).FindProperty("Name"));
					var list = ve.Q<ListView>();
					list.BindProperty(new SerializedObject(evt.newValue).FindProperty("Colliders"));
				});
				//ve.Q<TextField>().RegisterValueChangedCallback(evt => {
				//	ve.name = evt.newValue;
				//});
				return ve;
			};
			SpringBoneExColliderGroupsList.bindItem = (ve,i) => {
				ve.Q<ObjectField>().BindProperty(m_VRMInstance.FindProperty($"{m_spring}.ColliderGroups.Array.data[{i}]"));
				if (ve.Q<ObjectField>().value == null) return;
				ve.Q<TextField>().BindProperty(new SerializedObject(m_VRMInstance.FindProperty($"{m_spring}.ColliderGroups.Array.data[{i}]").objectReferenceValue).FindProperty("Name"));
				var list = ve.Q<ListView>();
				list.BindProperty(new SerializedObject(m_VRMInstance.FindProperty($"{m_spring}.ColliderGroups.Array.data[{i}]").objectReferenceValue).FindProperty("Colliders"));
			};
			SpringBoneExColliderGroupsList.itemsAdded += (o) => {
				
				Debug.Log(o.Distinct().Count());
				
				//Debug.Log(SpringBoneExColliderGroupsList.itemsSource[o.First() - 1]);
				
			};
			
			root.Q<Toggle>("DrawColliders").RegisterValueChangedCallback(evt => {
				m_instances.ToList().ForEach(o => o.Spring.Joints.ForEach(o => o.m_drawCollider = evt.newValue));
			});

			void SetSliderGroupBoxRegister(string name, string propatyname, string fieldname, VRM10SpringBoneEx instance)
			{
				var Curve = root.Q<CurveField>(name);
				var Slider = root.Q<Slider>(name);
				var Propaty = typeof(VRM10SpringBoneEx).GetProperty(propatyname);
				var CurveToggleButton = root.Q<Button>(name);
				void SetCurveDisplay(){
					Curve.style.display = (bool)Propaty.GetValue(instance) ?
						DisplayStyle.Flex : DisplayStyle.None;
				}
				void SetCurveToggleButtonStyle(){
					CurveToggleButton.text = (bool)Propaty.GetValue(instance) ?
						"X" : "C";
				}
				void SetCurveConextMenu(){
					Curve.AddManipulator(new ContextualMenuManipulator(evt => {
						evt.menu.AppendAction("Copy",action => {AnimationCurveUtility.Buffer = Curve.value;},DropdownMenuAction.AlwaysEnabled);
						evt.menu.AppendAction("Paste",action => {Curve.value = AnimationCurveUtility.Buffer;}, AnimationCurveUtility.Buffer == null ? DropdownMenuAction.AlwaysDisabled : DropdownMenuAction.AlwaysEnabled);
					}));
				}
				void GetSpringSettings()
				{
					var keycount = instance.Spring.Joints.Select(o => typeof(VRM10SpringBoneJoint).GetField(fieldname).GetValue(o)).Distinct().Count();
					Propaty.SetValue(instance, keycount > 1);
				}
				void SetCurve()
				{
					AnimationCurve curve = new AnimationCurve();
					//Curve.value.ClearKeys();
					instance.Spring.Joints.Select((o,index) => ((float)typeof(VRM10SpringBoneJoint).GetField(fieldname).GetValue(o),index)).Where(o => o.index < instance.Spring.Joints.Count - 1).ToList().ForEach(o => curve.AddKey(o.index,o.Item1));
					Curve.ranges = new Rect(0,Slider.lowValue,1,Slider.highValue);
					Curve.renderMode = CurveField.RenderMode.Mesh;
					Curve.value = AnimationCurveUtility.NormalizeCurveTime(curve);
				}
				GetSpringSettings();
				SetCurve();
				
				Curve.RegisterValueChangedCallback(evt => {
					
				});
				
				SetCurveDisplay();
				SetCurveToggleButtonStyle();
				SetCurveConextMenu();
				CurveToggleButton.clicked += () => {
					Propaty.SetValue(instance,!(bool)Propaty.GetValue(instance));
					SetCurveDisplay();
					SetCurveToggleButtonStyle();
				};
				Slider.RegisterValueChangedCallback(evt => {
					if (instance.Spring.Joints.Count == 0) return;
					typeof(VRM10SpringBoneJoint).GetField(fieldname).SetValue(
						instance.Spring.Joints[0],
						evt.newValue);
					var curve = Curve.value;
					var add = curve.AddKey(0,evt.newValue);
					if (add < 0) curve.MoveKey(0,new Keyframe(0,evt.newValue));
					Curve.SetValueWithoutNotify(curve);
					
					if (!(bool)Propaty.GetValue(instance))
					{
						instance.Spring.Joints.ForEach(o => typeof(VRM10SpringBoneJoint).GetField(fieldname).SetValue(o,evt.newValue));
						Curve.SetValueWithoutNotify(new AnimationCurve(new Keyframe[]{new Keyframe(){value = evt.newValue},new Keyframe(){time = 1,value = evt.newValue}}));
					}
				});
				Curve.RegisterValueChangedCallback(evt => {
					instance.Spring.Joints.Select((obj,index) => (obj,index)).ToList()
						.ForEach(o => typeof(VRM10SpringBoneJoint).GetField(fieldname)
						.SetValue(o.obj,
						evt.newValue.Evaluate((float)o.index / (instance.Spring.Joints.Count - 2))));
					Slider.SetValueWithoutNotify(evt.newValue.Evaluate(0));
				});
			}
			void SetVector3GroupBoxRegister(string name, string propatyname, string fieldname, VRM10SpringBoneEx instance)
			{
				var CurveBox = root.Q<GroupBox>(name).Q<GroupBox>(name + "XYZ");
				var Vector = CurveBox.Q<Vector3Field>(name);
				var Propaty = typeof(VRM10SpringBoneEx).GetProperty(propatyname);
				var CurveToggleButton = root.Q<Button>(name);
				void SetCurveDisplay(){
					CurveBox.style.display = (bool)Propaty.GetValue(instance) ?
						DisplayStyle.Flex : DisplayStyle.None;
				}
				void SetCurveToggleButtonStyle(){
					CurveToggleButton.text = (bool)Propaty.GetValue(instance) ?
						"X" : "C";
				}
				void GetSpringSettings()
				{
					var keycount = instance.Spring.Joints.Select(o => typeof(VRM10SpringBoneJoint).GetField(fieldname).GetValue(o)).Distinct().Count();
					Propaty.SetValue(instance, keycount > 1);
				}
				void SetCurve()
				{
					CurveBox.Query<CurveField>().ToList().Select((value,i) => (value,i)).ToList().ForEach(o => {
						AnimationCurve curve = new AnimationCurve();
						instance.Spring.Joints.Select((joint,index) => ((Vector3)typeof(VRM10SpringBoneJoint).GetField(fieldname).GetValue(joint),index)).ToList().ForEach(p => curve.AddKey(p.index,p.Item1[o.i]));
						o.value.ranges = new Rect(0,-1,1,2);
						o.value.renderMode = CurveField.RenderMode.Mesh;
						o.value.value = AnimationCurveUtility.NormalizeCurveTime(curve);
					});
				}
				GetSpringSettings();
				SetCurve();
				
				SetCurveDisplay();
				SetCurveToggleButtonStyle();
				root.Q<Button>(name).clicked += () => {
					Propaty.SetValue(instance,!(bool)Propaty.GetValue(instance));
					SetCurveDisplay();
					SetCurveToggleButtonStyle();
				};
			}
			foreach (var instance in m_instances)
			{
				SetSliderGroupBoxRegister("StiffnessForce", nameof(m_instance.StiffnessForceCurveValidate), "m_stiffnessForce", instance);
				SetSliderGroupBoxRegister("GravityPower", nameof(m_instance.GravityPowerCurveValidate), "m_gravityPower", instance);
				SetVector3GroupBoxRegister("GravityDir", nameof(m_instance.GravityDirCurveValidate), "m_gravityDir", instance);
				SetSliderGroupBoxRegister("DragForce", nameof(m_instance.DragForceCurveValidate), "m_dragForce", instance);
				SetSliderGroupBoxRegister("JointRadius", nameof(m_instance.JointRadiusCurveValidate), "m_jointRadius", instance);	
			}
			
			
			root.Q<ObjectField>("Center").BindProperty(m_VRMInstance.FindProperty($"{m_spring}.Center"));	
			root.Q<ObjectField>("Center").RegisterValueChangedCallback(evt => {
				foreach (var instance in m_instances)
				{
					m_VRM10Instance.SpringBone.Springs[instance.SpringIndex].Center = evt.newValue as Transform;
				}
			});
			
			var DefaultInspector = new Foldout(){text = "DefaultInspector",value = false};
			DefaultInspector.Add(new IMGUIContainer(() => DrawDefaultInspector()));
			root.Add(DefaultInspector);
			
			return root;
		}
		
		void Init()
		{
			m_instance = this.target as VRM10SpringBoneEx;
			m_VRM10Instance = m_instance.GetComponentInParent<Vrm10Instance>();
			
			m_VRMInstance = new SerializedObject(m_VRM10Instance);
			m_spring = $"SpringBone.Springs.Array.data[{m_instance.SpringIndex}]";
			Debug.Log(m_spring);
			m_instances = this.targets.ToList().Select(o => o as VRM10SpringBoneEx).ToArray();
			m_springs = m_instances.ToList().Select(o => $"SpringBone.Springs.Array.data[{o.SpringIndex}]").ToArray();
			m_instances.ToList().ForEach(o => {
				var instance = o as VRM10SpringBoneEx;
				var spring = $"SpringBone.Springs.Array.data[{instance.SpringIndex}]";
				Debug.Log(spring);
			});
			
			//Debug.Log(targetVRM);
			
			//var targetJoint = targetVRM.SpringBone.Springs[0].Joints[0];
			//m_stiffnessSlider = targetJoint.m_stiffnessForce;
			//m_gravitySlider = targetJoint.m_gravityPower;
			//m_dragSlider = targetJoint.m_dragForce;
			//m_radiusSlider = targetJoint.m_jointRadius;
		}
	}
}
