using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace colloid.VRM10Ex.Utility
{
	public sealed class Curve01Attribute : PropertyAttribute
	{
	}
	
	[CustomPropertyDrawer( typeof( Curve01Attribute ) )]
	public sealed class Curve01Drawer : PropertyDrawer
	{
		public override void OnGUI
		(
			Rect               position,
			SerializedProperty property,
			GUIContent         label
		)
		{
			if ( property.propertyType != SerializedPropertyType.AnimationCurve ) return;

			var ranges = new Rect( 0, 0, 1, 1 );

			EditorGUI.CurveField
			(
				position: position,
				property: property,
				color: Color.cyan,
				ranges: ranges
			);
		}
	}
	
	public static class AnimationCurveUtility
	{
		static AnimationCurve s_buffer;
		public static AnimationCurve Buffer
		{
			get => s_buffer;
			set => s_buffer = value;
		}
		
		public static AnimationCurve NormalizeCurveTime(AnimationCurve curve)
		{
			var keys = curve.keys;
			if (keys.Length > 0)
			{
				var minTime = keys[0].time;
				var maxTime = minTime;
				for (var i = 0; i < keys.Length; ++i)
				{
					minTime = Mathf.Min(minTime, keys[i].time);
					maxTime = Mathf.Max(maxTime, keys[i].time);
				}
				var range = maxTime - minTime;
				var timeScale = range < 0.0001f ? 1 : 1 / range;
				for (int i = 0; i < keys.Length; ++i)
				{
					keys[i].time = (keys[i].time - minTime) * timeScale;
				}
				curve.keys = keys;
			}
			return curve;
		}
	}
}