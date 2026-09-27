package com.yopay.device

import android.content.ContentValues
import android.content.Context
import android.database.sqlite.SQLiteDatabase
import android.database.sqlite.SQLiteOpenHelper

data class QueuedEvent(
    val id: Long,
    val source: Int,
    val senderId: String,
    val body: String,
    val receivedAt: Long
)

/**
 * The observations waiting to be uploaded.
 *
 * Plain SQLite rather than Room: three columns do not need a code generator, and a
 * dependency that regenerates source at build time is one more thing that can stop a
 * merchant's phone from reporting a payment.
 *
 * Nothing is deleted until the server has acknowledged it. That makes uploads
 * at-least-once, which is the right way round - a duplicate costs the server one hash
 * comparison, and a lost message costs a merchant a sale.
 */
class EventQueue(context: Context) : SQLiteOpenHelper(context, "yopay.db", null, 1) {

    override fun onCreate(db: SQLiteDatabase) {
        db.execSQL(
            "CREATE TABLE events (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " +
                "source INTEGER NOT NULL, " +
                "sender_id TEXT NOT NULL, " +
                "body TEXT NOT NULL, " +
                "received_at INTEGER NOT NULL)"
        )

        // The same message reaches the listener and the SMS receiver within a second of
        // each other. One row, not two.
        db.execSQL("CREATE UNIQUE INDEX ux_events ON events (sender_id, body, received_at)")
    }

    override fun onUpgrade(db: SQLiteDatabase, oldVersion: Int, newVersion: Int) = Unit

    fun add(source: Int, senderId: String, body: String, receivedAtSeconds: Long) {
        val values = ContentValues().apply {
            put("source", source)
            put("sender_id", senderId)
            put("body", body)
            put("received_at", receivedAtSeconds)
        }

        // CONFLICT_IGNORE rather than a read-then-write: the two capture paths can arrive
        // on different threads at the same moment.
        writableDatabase.insertWithOnConflict(
            "events", null, values, SQLiteDatabase.CONFLICT_IGNORE
        )
    }

    fun take(limit: Int): List<QueuedEvent> {
        val events = mutableListOf<QueuedEvent>()

        readableDatabase.rawQuery(
            "SELECT id, source, sender_id, body, received_at FROM events ORDER BY id LIMIT ?",
            arrayOf(limit.toString())
        ).use { cursor ->
            while (cursor.moveToNext()) {
                events.add(
                    QueuedEvent(
                        id = cursor.getLong(0),
                        source = cursor.getInt(1),
                        senderId = cursor.getString(2),
                        body = cursor.getString(3),
                        receivedAt = cursor.getLong(4)
                    )
                )
            }
        }

        return events
    }

    /** Called only after the server has said it has them. */
    fun remove(ids: List<Long>) {
        if (ids.isEmpty()) return

        writableDatabase.execSQL(
            "DELETE FROM events WHERE id IN (${ids.joinToString(",")})"
        )
    }

    fun count(): Int =
        readableDatabase.rawQuery("SELECT COUNT(*) FROM events", null).use { cursor ->
            if (cursor.moveToFirst()) cursor.getInt(0) else 0
        }
}
