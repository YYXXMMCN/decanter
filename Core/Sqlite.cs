// Sqlite.cs — 最小 SQLite 封装（P/Invoke sqlite3.dll），无第三方依赖。
// 逻辑层：不引用任何 System.Windows.Forms。
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Decanter.Core
{
    internal static class Native
    {
        public const int SQLITE_OK = 0;
        public const int SQLITE_ROW = 100;
        public const int SQLITE_DONE = 101;
        public const int SQLITE_OPEN_READWRITE = 0x00000002;
        public const int SQLITE_OPEN_CREATE = 0x00000004;
        public const int SQLITE_OPEN_FULLMUTEX = 0x00010000;
        public static readonly IntPtr SQLITE_TRANSIENT = new IntPtr(-1);

        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_close_v2(IntPtr db);
        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_busy_timeout(IntPtr db, int ms);
        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr cb, IntPtr arg, out IntPtr errmsg);
        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void sqlite3_free(IntPtr p);
        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr sqlite3_errmsg(IntPtr db);
        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int nByte, out IntPtr stmt, IntPtr tail);
        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_step(IntPtr stmt);
        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_reset(IntPtr stmt);
        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_finalize(IntPtr stmt);
        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_bind_text(IntPtr stmt, int idx, byte[] text, int n, IntPtr destructor);
        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_bind_int64(IntPtr stmt, int idx, long v);
        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_bind_null(IntPtr stmt, int idx);
        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);
        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_column_bytes(IntPtr stmt, int col);
        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern long sqlite3_column_int64(IntPtr stmt, int col);
        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int sqlite3_column_type(IntPtr stmt, int col);
        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern long sqlite3_last_insert_rowid(IntPtr db);
        [DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr sqlite3_libversion();

        public static byte[] U8(string s)
        {
            if (s == null) s = "";
            byte[] raw = Encoding.UTF8.GetBytes(s);
            byte[] z = new byte[raw.Length + 1];
            Array.Copy(raw, z, raw.Length);
            return z;
        }
    }

    public sealed class SqliteDb : IDisposable
    {
        private IntPtr _db;
        public string Path { get; private set; }

        public SqliteDb(string path)
        {
            Path = path;
            IntPtr db;
            int rc = Native.sqlite3_open_v2(Native.U8(path), out db,
                Native.SQLITE_OPEN_READWRITE | Native.SQLITE_OPEN_CREATE | Native.SQLITE_OPEN_FULLMUTEX, IntPtr.Zero);
            if (rc != Native.SQLITE_OK) throw new Exception("sqlite open failed: " + rc + " path=" + path);
            _db = db;
            Native.sqlite3_busy_timeout(_db, 5000);
        }

        public IntPtr Handle { get { return _db; } }

        public string ErrorMessage()
        {
            IntPtr p = Native.sqlite3_errmsg(_db);
            return Marshal.PtrToStringAnsi(p);
        }

        public void Exec(string sql)
        {
            IntPtr err;
            int rc = Native.sqlite3_exec(_db, Native.U8(sql), IntPtr.Zero, IntPtr.Zero, out err);
            if (rc != Native.SQLITE_OK)
            {
                string msg = err != IntPtr.Zero ? Marshal.PtrToStringAnsi(err) : ErrorMessage();
                if (err != IntPtr.Zero) Native.sqlite3_free(err);
                throw new Exception("sqlite exec failed: " + msg + " sql=" + sql);
            }
        }

        public Stmt Prepare(string sql)
        {
            IntPtr stmt;
            int rc = Native.sqlite3_prepare_v2(_db, Native.U8(sql), -1, out stmt, IntPtr.Zero);
            if (rc != Native.SQLITE_OK) throw new Exception("sqlite prepare failed: " + ErrorMessage() + " sql=" + sql);
            return new Stmt(stmt);
        }

        public long LastInsertRowId() { return Native.sqlite3_last_insert_rowid(_db); }

        public static string Version()
        {
            return Marshal.PtrToStringAnsi(Native.sqlite3_libversion());
        }

        public void Dispose()
        {
            if (_db != IntPtr.Zero) { Native.sqlite3_close_v2(_db); _db = IntPtr.Zero; }
        }
    }

    public sealed class Stmt : IDisposable
    {
        private IntPtr _stmt;
        public Stmt(IntPtr stmt) { _stmt = stmt; }

        public Stmt Bind(int idx, string v)
        {
            if (v == null) { Native.sqlite3_bind_null(_stmt, idx); return this; }
            byte[] b = Native.U8(v);
            Native.sqlite3_bind_text(_stmt, idx, b, b.Length - 1, Native.SQLITE_TRANSIENT);
            return this;
        }
        public Stmt Bind(int idx, long v) { Native.sqlite3_bind_int64(_stmt, idx, v); return this; }
        public Stmt BindNull(int idx) { Native.sqlite3_bind_null(_stmt, idx); return this; }

        public bool Step()
        {
            int rc = Native.sqlite3_step(_stmt);
            if (rc == Native.SQLITE_ROW) return true;
            if (rc == Native.SQLITE_DONE) return false;
            throw new Exception("sqlite step failed: " + rc);
        }

        public string GetText(int col)
        {
            if (Native.sqlite3_column_type(_stmt, col) == 5) return null; // NULL
            IntPtr p = Native.sqlite3_column_text(_stmt, col);
            if (p == IntPtr.Zero) return null;
            int n = Native.sqlite3_column_bytes(_stmt, col);
            byte[] buf = new byte[n];
            Marshal.Copy(p, buf, 0, n);
            return Encoding.UTF8.GetString(buf);
        }

        public long GetInt64(int col) { return Native.sqlite3_column_int64(_stmt, col); }
        public long? GetLongOrNull(int col)
        {
            if (Native.sqlite3_column_type(_stmt, col) == 5) return null;
            return Native.sqlite3_column_int64(_stmt, col);
        }

        public void Dispose()
        {
            if (_stmt != IntPtr.Zero) { Native.sqlite3_finalize(_stmt); _stmt = IntPtr.Zero; }
        }
    }
}
