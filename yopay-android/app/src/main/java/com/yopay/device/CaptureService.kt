package com.yopay.device

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.BatteryManager
import android.os.Build
import android.os.IBinder
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit

/**
 * Keeps the app alive and drains the queue.
 *
 * A foreground service with a permanent notification, because everything quieter gets
 * killed. Android will suspend a background process within minutes, and the manufacturer
 * skins this product has to run on are more aggressive than stock - so the choice is a
 * visible notification or a merchant who finds out at the end of the day that nothing was
 * captured since lunch.
 */
class CaptureService : Service() {

    private val worker = Executors.newSingleThreadScheduledExecutor()
    private lateinit var config: Config
    private lateinit var queue: EventQueue
    private lateinit var api: ApiClient

    override fun onCreate() {
        super.onCreate()

        config = Config(this)
        queue = EventQueue(this)
        api = ApiClient(config)

        startForeground(NOTIFICATION_ID, buildNotification("Watching for payments"))

        // Uploads run on a short tick, heartbeats on a slower one. Both are plain
        // scheduled work rather than WorkManager: this process is already required to stay
        // alive, so deferring anything to a scheduler that may run it in fifteen minutes
        // would only add latency to a payment the merchant is waiting on.
        worker.scheduleWithFixedDelay(::drain, 2, 5, TimeUnit.SECONDS)
        worker.scheduleWithFixedDelay(::sendHeartbeat, 5, 60, TimeUnit.SECONDS)
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        // START_STICKY: if the system kills this to reclaim memory, it comes back.
        return START_STICKY
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onDestroy() {
        worker.shutdownNow()
        super.onDestroy()
    }

    private fun drain() {
        if (!config.isPaired) return

        try {
            val batch = queue.take(50)
            if (batch.isEmpty()) return

            val result = api.upload(batch)

            if (result.isSuccess) {
                // Only now. Deleting before the acknowledgement would turn a dropped
                // connection into a lost payment.
                queue.remove(batch.map { it.id })
                updateNotification("Uploaded ${batch.size}, ${queue.count()} waiting")
            } else {
                updateNotification("Upload failed (${result.code}), ${queue.count()} waiting")
            }
        } catch (e: Exception) {
            // Offline, server down, phone asleep mid-request. The queue keeps the events
            // and the next tick tries again; nothing is thrown away on a bad connection.
            updateNotification("Offline, ${queue.count()} waiting")
        }
    }

    private fun sendHeartbeat() {
        if (!config.isPaired) return

        try {
            api.heartbeat(
                permissionState = if (Permissions.hasNotificationAccess(this)) 1 else 2,
                battery = batteryPercent(),
                network = "unknown"
            )
        } catch (e: Exception) {
            // A missed heartbeat is what tells the dashboard this handset went quiet. That
            // is the signal working, not a failure to handle.
        }
    }

    private fun batteryPercent(): Int? {
        val status = registerReceiver(null, IntentFilter(Intent.ACTION_BATTERY_CHANGED))
            ?: return null

        val level = status.getIntExtra(BatteryManager.EXTRA_LEVEL, -1)
        val scale = status.getIntExtra(BatteryManager.EXTRA_SCALE, -1)

        return if (level >= 0 && scale > 0) level * 100 / scale else null
    }

    private fun buildNotification(text: String): Notification {
        val manager = getSystemService(NotificationManager::class.java)

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            manager.createNotificationChannel(
                NotificationChannel(CHANNEL, "YoPay", NotificationManager.IMPORTANCE_LOW)
            )
        }

        return Notification.Builder(this, CHANNEL)
            .setContentTitle("YoPay")
            .setContentText(text)
            .setSmallIcon(android.R.drawable.stat_sys_upload)
            .setOngoing(true)
            .build()
    }

    private fun updateNotification(text: String) {
        getSystemService(NotificationManager::class.java)
            .notify(NOTIFICATION_ID, buildNotification(text))
    }

    companion object {
        private const val CHANNEL = "yopay-capture"
        private const val NOTIFICATION_ID = 1

        fun start(context: Context) {
            val intent = Intent(context, CaptureService::class.java)

            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                context.startForegroundService(intent)
            } else {
                context.startService(intent)
            }
        }

        /** Nudges the service so a payment is not sitting in the queue for five seconds. */
        fun requestUpload(context: Context) = start(context)
    }
}

class BootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action == Intent.ACTION_BOOT_COMPLETED && Config(context).isPaired) {
            CaptureService.start(context)
        }
    }
}
