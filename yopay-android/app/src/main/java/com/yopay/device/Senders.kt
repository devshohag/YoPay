package com.yopay.device

/**
 * Whose messages are worth capturing.
 *
 * This list is the only thing separating a real payment confirmation from a text anyone
 * can send that looks exactly like one. The server checks it again - a handset is not
 * trusted to be the last word on its own input - but filtering here keeps everything else
 * out of the queue and off the network.
 */
object Senders {

    private val allowed = setOf("bkash", "16247")

    fun isAllowed(sender: String?): Boolean =
        sender != null && allowed.contains(sender.trim().lowercase())

    /**
     * An incoming SMS usually surfaces as a notification from the messaging app, with the
     * sender as the title - so the title is what identifies it, not the package. The
     * package is recorded anyway, because during the device trial the useful question is
     * which handsets deliver these at all and through which app.
     */
    fun senderFromNotification(title: String?, packageName: String): String? {
        val candidate = title?.trim()
        if (isAllowed(candidate)) return candidate

        return if (packageName.contains("bkash", ignoreCase = true)) "bKash" else null
    }
}
