import io
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

from wand_tools import find_adb, authorized_devices

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import setup_wand


class SetupTests(unittest.TestCase):
    def test_windows_finds_project_local_executable_in_path_with_spaces(self):
        with tempfile.TemporaryDirectory(prefix="wand setup ") as temporary:
            root = Path(temporary)
            adb = root / ".tools/platform-tools/adb.exe"
            adb.parent.mkdir(parents=True)
            adb.write_bytes(b"placeholder")
            self.assertEqual(find_adb(root=root, system="Windows"), str(adb))

    def test_windows_device_listing_filters_unauthorized_and_offline(self):
        listing = "List of devices attached\r\nQUEST\tdevice\r\nOTHER\tunauthorized\r\nOLD\toffline\r\n"
        with patch("wand_tools.subprocess.check_output", return_value=listing):
            self.assertEqual(authorized_devices("adb.exe"), ["QUEST"])

    def archive(self, files):
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as package:
            for name in files:
                package.writestr(name, b"test")
        stream.seek(0)
        return stream

    def test_windows_download_keeps_required_dlls(self):
        with tempfile.TemporaryDirectory() as temporary, \
                patch.object(setup_wand, "ROOT", Path(temporary)), \
                patch("setup_wand.urllib.request.urlopen", return_value=self.archive(
                    ["platform-tools/adb.exe", "platform-tools/AdbWinApi.dll"])):
            adb = Path(setup_wand.install_platform_tools("Windows"))
            self.assertTrue(adb.is_file())
            self.assertTrue(adb.with_name("AdbWinApi.dll").is_file())

    def test_download_rejects_archive_path_escape(self):
        with tempfile.TemporaryDirectory() as temporary, \
                patch.object(setup_wand, "ROOT", Path(temporary)), \
                patch("setup_wand.urllib.request.urlopen", return_value=self.archive(["../escape"])):
            with self.assertRaises(RuntimeError):
                setup_wand.install_platform_tools("Darwin")
