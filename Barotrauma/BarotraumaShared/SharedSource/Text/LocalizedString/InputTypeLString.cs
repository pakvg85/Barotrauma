﻿#nullable enable
using System;

namespace Barotrauma
{
    public class InputTypeLString : LocalizedString
    {
        private readonly LocalizedString nestedStr;
        public InputTypeLString(LocalizedString nStr) { nestedStr = nStr; }

        protected override bool MustRetrieveValue()
        {
            //TODO: check for config changes!
            return base.MustRetrieveValue();
        }

        public override bool Loaded => nestedStr.Loaded;
        public override void RetrieveValue()
        {
            cachedValue = nestedStr.Value;
#if CLIENT
            //TODO: server shouldn't have this type at all
            foreach (InputType? inputType in Enum.GetValues(typeof(InputType)))
            {
                if (!inputType.HasValue) { continue; }
                cachedValue = cachedValue.Replace($"[{inputType}]", GameSettings.CurrentConfig.KeyMap.KeyBindText(inputType.Value).Value, StringComparison.OrdinalIgnoreCase);
                cachedValue = cachedValue.Replace($"[InputType.{inputType}]", GameSettings.CurrentConfig.KeyMap.KeyBindText(inputType.Value).Value, StringComparison.OrdinalIgnoreCase);
            }
#endif
            UpdateLanguage();
        }
    }
}