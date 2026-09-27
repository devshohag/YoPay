package com.yopay.device

import android.app.Notification
import android.service.notification.NotificationListenerService
import android.service.notification.StatusBarNotification

/**
 * The primary capture path.
 *
 * Notification access rather than the SMS permission because it arrives sooner - the
 * notification is posted as the message lands - and because it asks the merchant for far
 * less. Reading every SMS on a phone to find four a day from one sender is a trade nobody
 * should have to make.
 *
 * Everything not from a whitelisted sender is dropped here and never stored.
 */
class PaymentNotificationListener : NotificationListenerService() {

    override fun onNotificationPosted(sbn: StatusBarNotification) {
        val extras = sbn.notification?.extras ?: return

        val title = extras.getCharSequence(Notification.EXTRA_TITLE)?.toString()
        val sender = Senders.senderFromNotification(title, sbn.packageName) ?: return

        val text = listOfNotNull(
            extras.getCharSequence(Notification.EXTRA_TEXT)?.toString(),
            extras.getCharSequence(Notification.EXTRA_BIG_TEXT)?.toString()
        ).maxByOrNull { it.length } ?: return

        if (text.isBlank()) return

        EventQueue(applicationContext).add(
            source = SOURCE_NOTIFICATION,
            senderId = sender,
            body = text,
            receivedAtSeconds = sbn.postTime / 1000
        )

        CaptureService.requestUpload(applicationContext)
    }

    override fun onListenerConnected() {
        CaptureService.start(applicationContext)
    }

    companion object {
        const val SOURCE_NOTIFICATION = 1
    }
}
