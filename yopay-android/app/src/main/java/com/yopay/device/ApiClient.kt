package com.yopay.device

import org.json.JSONArray
import org.json.JSONObject
import java.io.BufferedReader
import java.net.HttpURLConnection
import java.net.URL
import java.util.UUID

class ApiResult(val code: Int, val body: String) {
    val isSuccess: Boolean get() = code in 200..299
}

/**
 * Talks to the ingest service.
 *
 * HttpURLConnection on purpose. An HTTP client library would be more pleasant and would
 * also be a dependency whose version has to keep agreeing with everything else for as long
 * as this app is installed on someone's phone.
 */
class ApiClient(private val config: Config) {

    fun pair(code: String, publicKey: String, model: String): ApiResult {
        val body = JSONObject()
            .put("token", code)
            .put("publicKey", publicKey)
            .put("model", model)
            .put("appVersion", BuildInfo.VERSION)
            .toString()

        return post("/v1/ingest/pair", body, signed = false)
    }

    fun upload(events: List<QueuedEvent>): ApiResult {
        val array = JSONArray()

        for (event in events) {
            array.put(
                JSONObject()
                    .put("source", event.source)
                    .put("senderId", event.senderId)
                    .put("body", event.body)
                    .put("receivedAt", isoSeconds(event.receivedAt))
                    // Recomputed server side from the fields it stores. Sent only so the
                    // shape of the request matches the contract.
                    .put("dedupeHash", "")
            )
        }

        val body = JSONObject()
            .put("deviceFingerprint", config.deviceId)
            .put("events", array)
            .toString()

        return post("/v1/ingest/events", body, signed = true)
    }

    fun heartbeat(permissionState: Int, battery: Int?, network: String?): ApiResult {
        val body = JSONObject()
            .put("deviceFingerprint", config.deviceId)
            .put("appVersion", BuildInfo.VERSION)
            .put("permissionState", permissionState)
            .put("batteryPercent", battery ?: JSONObject.NULL)
            .put("networkType", network ?: JSONObject.NULL)
            .put("sentAt", isoSeconds(System.currentTimeMillis() / 1000))
            .toString()

        return post("/v1/ingest/heartbeat", body, signed = true)
    }

    private fun post(path: String, body: String, signed: Boolean): ApiResult {
        val connection = URL(config.ingestUrl + path).openConnection() as HttpURLConnection

        try {
            connection.requestMethod = "POST"
            connection.doOutput = true
            connection.connectTimeout = 15_000
            connection.readTimeout = 20_000
            connection.setRequestProperty("Content-Type", "application/json; charset=utf-8")

            if (signed) {
                val timestamp = System.currentTimeMillis() / 1000
                val nonce = UUID.randomUUID().toString().replace("-", "")
                val canonical = Canonical.build("POST", path, timestamp, nonce, body)

                connection.setRequestProperty("X-YoPay-Device", config.deviceId ?: "")
                connection.setRequestProperty("X-YoPay-Timestamp", timestamp.toString())
                connection.setRequestProperty("X-YoPay-Nonce", nonce)
                connection.setRequestProperty("X-YoPay-Signature", DeviceKey.sign(canonical))
            }

            connection.outputStream.use { it.write(body.toByteArray(Charsets.UTF_8)) }

            val code = connection.responseCode
            val stream = if (code in 200..299) connection.inputStream else connection.errorStream
            val text = stream?.bufferedReader()?.use(BufferedReader::readText) ?: ""

            return ApiResult(code, text)
        } finally {
            connection.disconnect()
        }
    }

    /** The server parses a timestamp, so send one it will read the same way. */
    private fun isoSeconds(epochSeconds: Long): String {
        val format = java.text.SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'", java.util.Locale.US)
        format.timeZone = java.util.TimeZone.getTimeZone("UTC")
        return format.format(java.util.Date(epochSeconds * 1000))
    }
}

object BuildInfo {
    const val VERSION = "1.0"
}
