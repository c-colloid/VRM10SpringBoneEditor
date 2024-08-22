using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace CustomUI
{
	public class ToggleButton : Button,INotifyValueChanged<bool>
{
	bool _check;
	
	public bool value {
		get => _check;
		set {
			if (_check == value)
			{
				return;
			}
			
			using (var pooled = ChangeEvent<bool>.GetPooled(_check, value))
			{
				pooled.target = this;
				
				SetValueWithoutNotify(value);
				
				SendEvent(pooled);
			}
		}
	}
	
	public ToggleButton()
	{
		clicked += () => {
			value = !value;
		};
	}
	
	public new class UxmlTraits : Button.UxmlTraits
	{
		private UxmlBoolAttributeDescription _checkValue = new UxmlBoolAttributeDescription{name = "value"};
		
		public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
		{
			base.Init(ve,bag,cc);
			var thisElement = (ToggleButton)ve;
			thisElement._check = _checkValue.GetValueFromBag(bag,cc);
		}
	}
	
	
	public new class UxmlFactory : UxmlFactory<ToggleButton, UxmlTraits>{}
	
	public void SetValueWithoutNotify(bool newValue)
	{
		_check = newValue;
	}
	
}
}