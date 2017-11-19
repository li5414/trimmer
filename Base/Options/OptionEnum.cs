﻿#if !NO_TRIMMER || UNITY_EDITOR

using System;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace sttz.Trimmer.BaseOptions
{

/// <summary>
/// Option base class with an enumeration as value.
/// </summary>
/// <remarks>
/// If the given enum type has the `Flags` attribute applied, the Option
/// automatically switches to Unity's flags/mask field. You can override 
/// this by setting <see cref="IsMask"/> in <see cref="Option.Configure"/>.
/// 
/// Note that prior to Unity 2017.3 (and its EnumFlagsField method), Unity's
/// handling of flags enum is severily limited and the enum's values must
/// be increasing powers of two without gaps.
/// </remarks>
public abstract class OptionEnum<TEnum> : Option<TEnum>
{
	#if UNITY_EDITOR
	public override string EditGUI(string input)
	{
		var enumValue = (Enum)(object)Parse(input);
		if (!IsMask) {
			enumValue = EditorGUILayout.EnumPopup(enumValue);
		} else {
			enumValue = EditorGUILayout.EnumMaskField(enumValue);
		}
		return Save((TEnum)(object)enumValue);
	}
	#endif

	/// <summary>
	/// Set wether the underlying enum is a flags enum and the value should be treated as a mask.
	/// </summary>
	/// <remarks>
	/// Set this property in the Option's <see cref="Option.Configure"/> method. If not set,
	/// the value defaults to `true` when the enumeration has the `Flags` attribute applied.
	/// </remarks>
	public bool IsMask {
		get {
			if (_isMask == null) {
				_isMask = typeof(TEnum).GetCustomAttributes(typeof(FlagsAttribute), false).Any();
			}
			return _isMask ?? false;
		}
		set {
			_isMask = value;
		}
	}
	private bool? _isMask;

	override public TEnum Parse(string input)
	{
		if (string.IsNullOrEmpty(input))
			return DefaultValue;

		try {
			return (TEnum)Enum.Parse(typeof(TEnum), input, true);
		} catch {
			Debug.LogError("Failed to parse enum value of " + typeof(TEnum).Name + ": " + input);
			return DefaultValue;
		}
	}

	override public void Load(string input)
	{
		Value = Parse(input);
	}

	override public string Save(TEnum input)
	{
		return ((Enum)(object)input).ToString("F");
	}

	override public string Save()
	{
		return Save(Value);
	}
}

}

#endif
