package com.yopay.device

import java.security.MessageDigest

/**
 * Builds the string the server will rebuild and check the signature against.
 *
 * It has to match byte for byte:
 *
 *     METHOD \n PATH_AND_QUERY \n UNIX_TIMESTAMP \n NONCE \n SHA256_HEX(BODY)
 *
 * The hex is upper case because that is what .NET's Convert.ToHexString produces, and a
 * mismatch here fails as "unauthorized" with nothing to say why - the single most
 * expensive kind of bug to chase across two platforms.
 */
object Canonical {

    fun build(method: String, path: String, timestamp: Long, nonce: String, body: String): String {
        val bodyHash = sha256Hex(body)
        return "${method.uppercase()}\n$path\n$timestamp\n$nonce\n$bodyHash"
    }

    fun sha256Hex(value: String): String {
        val digest = MessageDigest.getInstance("SHA-256")
            .digest(value.toByteArray(Charsets.UTF_8))

        val builder = StringBuilder(digest.size * 2)
        for (byte in digest) {
            builder.append("%02X".format(byte))
        }

        return builder.toString()
    }
}
