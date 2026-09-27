package com.yopay.device

import android.content.Context
import android.provider.Settings

object Permissions {

    /**
     * Whether the merchant has granted notification access.
     *
     * Checked on every heartbeat, not just at startup. A merchant clearing app data, a
     * system update, or a well-meaning cleaner app can revoke this at any time, and the
     * failure is silent: the app keeps running and simply stops seeing payments. The
     * dashboard has to be able to say so within minutes.
     */
    fun hasNotificationAccess(context: Context): Boolean {
        val enabled = Settings.Secure.getString(
            context.contentResolver, "enabled_notification_listeners"
        ) ?: return false

        return enabled.contains(context.packageName)
    }

    fun notificationAccessSettings(): android.content.Intent =
        android.content.Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS)
}
