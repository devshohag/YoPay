package com.yopay.device

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.provider.Telephony

/**
 * The fallback, off unless the merchant turns it on.
 *
 * Kept because the device trial may find a handset where notification access is
 * unreliable, and on that handset this is the difference between a working product and an
 * excuse. It is not the default: it asks for a permission that reads every message on the
 * phone, and most merchants should never have to grant it.
 */
class SmsReceiver : BroadcastReceiver() {

    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action != Telephony.Sms.Intents.SMS_RECEIVED_ACTION) return
        if (!Config(context).smsFallbackEnabled) return

        val messages = Telephony.Sms.Intents.getMessagesFromIntent(intent) ?: return
        if (messages.isEmpty()) return

        val sender = messages[0].originatingAddress
        if (!Senders.isAllowed(sender)) return

        // Long messages arrive split across parts and have to be put back together before
        // anything can be read out of them.
        val body = messages.joinToString("") { it.messageBody ?: "" }
        if (body.isBlank()) return

        EventQueue(context).add(
            source = SOURCE_SMS,
            senderId = sender ?: "unknown",
            body = body,
            receivedAtSeconds = messages[0].timestampMillis / 1000
        )

        CaptureService.requestUpload(context)
    }

    companion object {
        const val SOURCE_SMS = 2
    }
}
