package com.yopay.device

import android.os.Bundle
import android.widget.Button
import android.widget.EditText
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import org.json.JSONObject
import java.util.concurrent.Executors

/**
 * Two things: pair the handset, and show whether it is working.
 *
 * Deliberately plain. The merchant opens this once to pair and then only when something
 * looks wrong, and what they need in that moment is whether notification access is on and
 * how many messages are stuck - not a designed experience.
 */
class MainActivity : AppCompatActivity() {

    private val background = Executors.newSingleThreadExecutor()
    private lateinit var config: Config

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        config = Config(this)

        findViewById<EditText>(R.id.serverUrl).setText(config.ingestUrl)

        findViewById<Button>(R.id.pairButton).setOnClickListener { pair() }

        findViewById<Button>(R.id.permissionButton).setOnClickListener {
            startActivity(Permissions.notificationAccessSettings())
        }

        refresh()
    }

    override fun onResume() {
        super.onResume()
        refresh()
    }

    private fun pair() {
        val url = findViewById<EditText>(R.id.serverUrl).text.toString().trim()
        val code = findViewById<EditText>(R.id.pairingCode).text.toString().trim()

        if (code.isEmpty()) {
            Toast.makeText(this, "Enter the pairing code", Toast.LENGTH_SHORT).show()
            return
        }

        config.ingestUrl = url

        background.execute {
            try {
                val publicKey = DeviceKey.ensureExists()
                val result = ApiClient(config).pair(code, publicKey, android.os.Build.MODEL)

                runOnUiThread {
                    if (result.isSuccess) {
                        val json = JSONObject(result.body)
                        config.deviceId = json.getString("deviceId")
                        config.walletNumber = json.optString("walletNumber")
                        config.merchantName = json.optString("merchantName")

                        CaptureService.start(this)
                        Toast.makeText(this, "Paired", Toast.LENGTH_SHORT).show()
                        refresh()
                    } else {
                        Toast.makeText(
                            this, "Pairing failed: ${result.code} ${result.body}",
                            Toast.LENGTH_LONG
                        ).show()
                    }
                }
            } catch (e: Exception) {
                runOnUiThread {
                    Toast.makeText(this, "Cannot reach ${config.ingestUrl}", Toast.LENGTH_LONG).show()
                }
            }
        }
    }

    private fun refresh() {
        val status = findViewById<TextView>(R.id.status)
        val queued = EventQueue(this).count()
        val access = Permissions.hasNotificationAccess(this)

        status.text = buildString {
            appendLine(if (config.isPaired) "Paired" else "Not paired")
            if (config.isPaired) {
                appendLine("Merchant: ${config.merchantName}")
                appendLine("Wallet: ${config.walletNumber}")
            }
            appendLine()
            appendLine(if (access) "Notification access: on" else "Notification access: OFF")
            appendLine("Queued messages: $queued")
        }
    }
}
