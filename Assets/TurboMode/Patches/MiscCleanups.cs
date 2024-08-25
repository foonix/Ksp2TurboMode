using KSP.Messages;
using System;
using System.Collections.Generic;

namespace TurboMode.Patches
{
    public static class MiscCleanups
    {
        static readonly Dictionary<Type, ReflectionUtil.ClassClearer<MessageCenterMessage>> messageClearers = new();

        /// <summary>
        /// Garbage-free version of MessageCenterMessage.Clear()
        /// </summary>
        /// <param name="message"></param>
        public static void ClearMessage(MessageCenterMessage message)
        {
            var type = message.GetType();
            if (!messageClearers.TryGetValue(type, out var clearer))
            {
                clearer = ReflectionUtil.ClassClearer<MessageCenterMessage>.Create(type);
                messageClearers[type] = clearer;
            }
            clearer.Clear(message);
        }
    }
}