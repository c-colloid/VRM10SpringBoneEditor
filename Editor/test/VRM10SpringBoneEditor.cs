using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UniVRM10;

public class VRM10SpringBoneEditor : EditorWindow
{
	#region Variable
    [SerializeField]
	private StyleSheet m_StyleSheet = default;
	[SerializeField]
	private VisualTreeAsset m_UXML = default;
	[SerializeField]
	private VisualTreeAsset m_ColliderGroupsListViewUXML = default;
	[SerializeField]
	private ColliderGroupsListView m_ColliderGroupsListView = default;
	#endregion
	
	//ToolメニューからWindowを生成
	[MenuItem("Tools/VRM10SpringBoneEditor")]
    public static void Show()
    {
        VRM10SpringBoneEditor wnd = GetWindow<VRM10SpringBoneEditor>();
	    wnd.titleContent = new GUIContent("VRM10SpringBoneEditor");
    }
    
	//初期化
    public void CreateGUI()
	{
        // Each editor window contains a root VisualElement object
		VisualElement root = rootVisualElement;
		var scroller = new UnityEngine.UIElements.ScrollView(ScrollViewMode.Vertical);
		scroller.StretchToParentSize();
		root.Add(scroller);
		root = scroller;
		var tree = m_UXML.CloneTree();
		root.Add(tree);
		root.styleSheets.Add(m_StyleSheet);
		
		var rootBonesLV = root.Q<ListView>("RootBones");
		rootBonesLV.itemsSource = new List<Transform>();
		rootBonesLV.makeItem = () => new ObjectField(){objectType = typeof(Transform)};
		rootBonesLV.bindItem = (e,i) => (e as ObjectField).value = (Object)rootBonesLV.itemsSource[i];
		SetListViewHight(rootBonesLV);
		
		var colliderGroupsLV = root.Q<ListView>("ColliderGroups");
		var foldoutLV = m_ColliderGroupsListView.ColliderGroupsList;
		//colliderGroupsLV.itemsSource = foldoutLV;
		colliderGroupsLV.makeItem = () => m_ColliderGroupsListViewUXML.CloneTree();
		colliderGroupsLV.bindItem = (e,i) => BindItem(e,i);
		colliderGroupsLV.itemsAdded += items => {
			foldoutLV[items.Last()].text = "";
			foldoutLV[items.Last()].value.Clear();
		};
		SetListViewHight(colliderGroupsLV);
		
		var so = new SerializedObject(m_ColliderGroupsListView);
		colliderGroupsLV.Bind(so);
	}
    
	//ColliderGroupsにアイテムをBind
	void BindItem(VisualElement elm,int i)
	{
		var thisItem = m_ColliderGroupsListView.ColliderGroupsList[i];
		var text = elm.Q<TextField>("ColliderGroupName");
		text.value = thisItem.text;
		
		var listView = elm.Q<ListView>("Colliders");
		listView.headerTitle = string.IsNullOrEmpty(text.value) ? "Colliders" + i : text.value;
		//text.RegisterCallback<ChangeEvent<string>>(x => {
		//	thisItem.text = x.newValue;
		//	if (string.IsNullOrEmpty(x.newValue)) {
		//		listView.headerTitle = "Colliders";
		//		return;
		//	}
		//	listView.headerTitle = x.newValue;
		//});
		text.RegisterValueChangedCallback(x => {
			thisItem.text = x.newValue;
			if (string.IsNullOrEmpty(x.newValue)) {
				listView.headerTitle = "Colliders" + i;
				//thisItem.text = "";
				return;
			}
			listView.headerTitle = x.newValue;
		});
		listView.Q<Foldout>().viewDataKey = $"unity-list-ColliderGroupsList.Array.data[{i}].Toggle.value";
		
		listView.itemsSource = thisItem.value;
		listView.makeItem = () => new ObjectField(){objectType = typeof(VRM10SpringBoneCollider)};
		listView.bindItem = (e,j) => (e as ObjectField).value = (Object)listView.itemsSource[j];
	}
	
	void SetListViewHight(ListView lv)
	{
		//lv.style.minHeight = lv.Q<Toggle>().value ? 70 : 20;
		//lv.Q<Toggle>().RegisterValueChangedCallback(x => {
		//	lv.style.minHeight = lv.Q<Toggle>().value ? 70 : 20;
		//});
	}
}
