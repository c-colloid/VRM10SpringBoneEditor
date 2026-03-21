using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using colloid.VRM10Ex.Utility;

namespace CustomUI
{
	
public class SliderWithCurve : Slider
{
	ToggleButton _toggleButton;
	[Curve01]
	CurveField _curve;

	// showMixedValue 自前実装（DOM再構築により基底の実装が壊れるため）
	bool _showMixedValue;

	public new bool showMixedValue
	{
		get => _showMixedValue;
		set
		{
			_showMixedValue = value;
			// 内蔵テキスト入力の showMixedValue を設定
			// Unity バージョンにより FloatField または TextField の場合がある
			var input = this.Q(className: "unity-base-slider__text-field")
				?? this.Q<FloatField>()
				?? (VisualElement)this.Q<TextField>();
			if (input is FloatField ff)
				ff.showMixedValue = value;
			else if (input is TextField tf)
				tf.showMixedValue = value;
			// dragger thumb を非表示
			var dragger = this.Q("unity-dragger");
			if (dragger != null)
				dragger.style.visibility = value ? Visibility.Hidden : Visibility.Visible;
		}
	}

	string _buttonText;
	public string buttonText {
		get => _buttonText;
		set {
			if (_buttonText == value)
			{
				return;
			}
			using (var pooled = ChangeEvent<string>.GetPooled(_buttonText, value))
			{
				pooled.target = this;
				
				SendEvent(pooled);
			}
		}
	}
	bool _buttonValue;
	public bool buttonValue => _buttonValue;

	public SliderWithCurve()
	{
		Add(new VisualElement(){name = $"{nameof(SliderWithCurve)}-Container",style = {flexShrink = 0,flexGrow = 1}});
		var root = this.Q<VisualElement>($"{nameof(SliderWithCurve)}-Container");
		root.Add(this.Q<VisualElement>("unity-drag-container").parent);
		this.Q<VisualElement>("unity-drag-container").parent.style.height = Length.Auto();
		_toggleButton = new ToggleButton();
		_curve = new CurveField(){ranges = new Rect(0,0,1,1)};
		_curve.style.flexGrow = 1;
		_curve.value = AnimationCurve.Constant(0,1,1);
		Add(_toggleButton);
		root.Add(_curve);
		
		_toggleButton.text = _buttonText;
		_toggleButton.value = _buttonValue;
		
		//ToggleButtonでCurveの表示・非表示切り替え
		_toggleButton.RegisterValueChangedCallback<bool>(x => {
			_buttonValue = x.newValue;
			_curve.style.display = x.newValue ? DisplayStyle.Flex : DisplayStyle.None;
			_buttonText = _toggleButton.text = _buttonValue ? "X" : "C";
		});
	}
	
	public new class UxmlTraits : Slider.UxmlTraits
	{
		private UxmlStringAttributeDescription _text = new UxmlStringAttributeDescription(){name = "button-text",defaultValue = "X"};
		private UxmlBoolAttributeDescription _check = new UxmlBoolAttributeDescription(){name = "button-value",defaultValue = true};
		
		public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
		{
			base.Init(ve,bag,cc);
			var thisElement = (SliderWithCurve)ve;
			thisElement._buttonText = thisElement._toggleButton.text = _text.GetValueFromBag(bag,cc);
			thisElement._buttonValue = thisElement._toggleButton.value = _check.GetValueFromBag(bag,cc);
			thisElement.Q<CurveField>().style.display = _check.GetValueFromBag(bag,cc) ? DisplayStyle.Flex : DisplayStyle.None;
		}
	}
	
	public new class UxmlFactory : UxmlFactory<SliderWithCurve, UxmlTraits>{}
}

}