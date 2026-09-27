package com.yopay.device

import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.Signature
import java.security.spec.ECGenParameterSpec

/**
 * The handset's identity.
 *
 * The key is generated inside the Android Keystore and never leaves it - not into this
 * process, not into a backup, not off a rooted phone. Everything signs by handing bytes to
 * the Keystore and taking back a signature; the private half has no representation in the
 * app at all.
 *
 * That is the whole reason the server holds a public key rather than a shared secret: if
 * YoPay's database were stolen tomorrow, every merchant's API secret would be in it, and
 * not one device's signing key.
 */
object DeviceKey {

    private const val ALIAS = "yopay-device"
    private const val KEYSTORE = "AndroidKeyStore"

    /** Creates the key once. Calling it again is harmless and returns the existing one. */
    fun ensureExists(): String {
        val keyStore = KeyStore.getInstance(KEYSTORE).apply { load(null) }

        if (!keyStore.containsAlias(ALIAS)) {
            val generator = KeyPairGenerator.getInstance(
                KeyProperties.KEY_ALGORITHM_EC, KEYSTORE
            )

            generator.initialize(
                KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_SIGN)
                    .setAlgorithmParameterSpec(ECGenParameterSpec("secp256r1"))
                    .setDigests(KeyProperties.DIGEST_SHA256)
                    // No user authentication requirement: this service has to sign while
                    // the phone sits locked on a shelf, which is exactly where it will be.
                    .build()
            )

            generator.generateKeyPair()
        }

        return publicKeyBase64()
    }

    /** X.509 SubjectPublicKeyInfo, which is what the server imports directly. */
    fun publicKeyBase64(): String {
        val keyStore = KeyStore.getInstance(KEYSTORE).apply { load(null) }
        val certificate = keyStore.getCertificate(ALIAS)
            ?: error("Device key is missing. Pair again.")

        return Base64.encodeToString(certificate.publicKey.encoded, Base64.NO_WRAP)
    }

    /**
     * Signs with SHA256withECDSA, which on Android produces a DER sequence. The server
     * accepts that and .NET's fixed-width form both, so neither side has to re-encode
     * what the other wrote.
     */
    fun sign(canonical: String): String {
        val keyStore = KeyStore.getInstance(KEYSTORE).apply { load(null) }
        val entry = keyStore.getEntry(ALIAS, null) as? KeyStore.PrivateKeyEntry
            ?: error("Device key is missing. Pair again.")

        val signature = Signature.getInstance("SHA256withECDSA").apply {
            initSign(entry.privateKey)
            update(canonical.toByteArray(Charsets.UTF_8))
        }

        return Base64.encodeToString(signature.sign(), Base64.NO_WRAP)
    }

    fun delete() {
        val keyStore = KeyStore.getInstance(KEYSTORE).apply { load(null) }
        if (keyStore.containsAlias(ALIAS)) {
            keyStore.deleteEntry(ALIAS)
        }
    }
}
