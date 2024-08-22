using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UniVRM10;

[CreateAssetMenu]
public class ColliderGroupsListView : ScriptableObject
{
	public List<ColliderGroup> ColliderGroupsList;
	
	[Serializable]
	public class ColliderGroup
	{
		public string text;
		public List<VRM10SpringBoneCollider> value = new List<VRM10SpringBoneCollider>(1);
	}
}
