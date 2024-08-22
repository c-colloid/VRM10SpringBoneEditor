using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class TestSlider : EditorWindow
{
	[SerializeField]
	VisualTreeAsset UXML = default;
	
	[MenuItem("Tools/Test/TestSlider")]
    public static void ShowExample()
    {
        TestSlider wnd = GetWindow<TestSlider>();
        wnd.titleContent = new GUIContent("TestSlider");
    }

    public void CreateGUI()
    {
        // Each editor window contains a root VisualElement object
        VisualElement root = rootVisualElement;

	    root.Add(UXML.CloneTree());
    }
}
