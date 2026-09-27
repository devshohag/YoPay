package com.yopay.device

import android.content.Context
import android.content.SharedPreferences

/**
 * Settings that survive a restart: where the server is, and who this handset is once it
 * has paired.
 *
 * The private key is deliberately not here. It lives in the Android Keystore and this
 * file only ever holds the device id, which is public.
 */
class Config(context: Context) {

    private val prefs: SharedPreferences =
        context.getSharedPreferences("yopay", Context.MODE_PRIVATE)

    var ingestUrl: String
        get() = prefs.getString(KEY_URL, DEFAULT_URL) ?: DEFAULT_URL
        set(value) = prefs.edit().putString(KEY_URL, value.trimEnd('/')).apply()

    var deviceId: String?
        get() = prefs.getString(KEY_DEVICE_ID, null)
        set(value) = prefs.edit().putString(KEY_DEVICE_ID, value).apply()

    var walletNumber: String?
        get() = prefs.getString(KEY_WALLET, null)
        set(value) = prefs.edit().putString(KEY_WALLET, value).apply()

    var merchantName: String?
        get() = prefs.getString(KEY_MERCHANT, null)
        set(value) = prefs.edit().putString(KEY_MERCHANT, value).apply()

    var smsFallbackEnabled: Boolean
        get() = prefs.getBoolean(KEY_SMS, false)
        set(value) = prefs.edit().putBoolean(KEY_SMS, value).apply()

    val isPaired: Boolean get() = deviceId != null

    companion object {
        /** 10.0.2.2 is the host machine as seen from the Android emulator. A real handset
         *  needs the laptop's address on the same wifi - set it on the pairing screen. */
        const val DEFAULT_URL = "http://10.0.2.2:5081"

        private const val KEY_URL = "ingest_url"
        private const val KEY_DEVICE_ID = "device_id"
        private const val KEY_WALLET = "wallet_number"
        private const val KEY_MERCHANT = "merchant_name"
        private const val KEY_SMS = "sms_fallback"
    }
}
