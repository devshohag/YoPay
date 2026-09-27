// Versions are pinned rather than left to the IDE so a fresh clone builds the same way.
// Android Studio will offer to upgrade them; accepting is fine, and the upgrade assistant
// handles the Gradle wrapper at the same time.
plugins {
    id("com.android.application") version "8.7.3" apply false
    id("org.jetbrains.kotlin.android") version "2.0.21" apply false
}
