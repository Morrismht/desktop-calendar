using System;
using System.Media;
using System.Runtime.InteropServices;

namespace DesktopCalendar.Services
{
    /// <summary>
    /// 提醒音：播放 Windows 当前的「通知」方案音（跟随系统音量、勿扰与声音主题），
    /// 别名不可用时退回系统提示音。全程吞掉异常——响不了绝不能影响提醒卡片。
    /// </summary>
    public static class ReminderSound
    {
        private const uint SND_ASYNC = 0x0001;      // 不阻塞调用线程
        private const uint SND_NODEFAULT = 0x0002;  // 别名找不到时不放默认音，让我们能判断失败
        private const uint SND_ALIAS = 0x00010000;  // 当成 AppEvents 别名解析

        // Win10/11 上「通知」这个声音在不同版本里的别名不一样，逐个试，先命中的就用。
        private static readonly string[] Aliases = new string[]
        {
            "Notification.Default",
            "SystemNotification",
            "Notifications",
            "Notification.IM"
        };

        private static bool _enabled = true;

        /// <summary>是否允许出声（状态条「提醒声音」开关控制）。</summary>
        public static bool Enabled
        {
            get { return _enabled; }
            set { _enabled = value; }
        }

        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);

        /// <summary>播一次提醒音；关掉时直接返回 false。</summary>
        public static bool Play()
        {
            if (!_enabled) return false;

            for (int i = 0; i < Aliases.Length; i++)
            {
                try
                {
                    if (PlaySound(Aliases[i], IntPtr.Zero, SND_ALIAS | SND_ASYNC | SND_NODEFAULT))
                    {
                        return true;
                    }
                }
                catch (Exception)
                {
                }
            }

            try
            {
                SystemSounds.Asterisk.Play();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
