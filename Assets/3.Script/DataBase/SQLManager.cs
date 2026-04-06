using System;
using System.Security.Cryptography;
using Mirror;
using MySql.Data.MySqlClient;
using UnityEngine;

// 회원가입 상태
public enum RegisterResult
{
    Success,
    InvalidInput,
    DuplicateLoginId,
    DuplicateNickname,
    Failed
}

// 로그인 상태
public enum LoginResult
{
    Success,
    InvalidInput,
    UserNotFound,
    WrongPassword,
    Failed
}

public class SQLManager : NetworkBehaviour
{
    public static SQLManager Instance;

    [Header("DB Settings")]
    [SerializeField] private string server = "127.0.0.1";
    [SerializeField] private int port = 3306;
    [SerializeField] private string database = "last_attraction_db";
    [SerializeField] private string user = "gameuser"; // 처음엔 root로 테스트 가능
    [SerializeField] private string password = "your_password";

    private string connectionString;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        connectionString = $"Server={server};Port={port};Database={database};User ID={user};Password={password};";

        TestConnection();
    }

    [Server]
    private void TestConnection()
    {
        try
        {
            var connection = new MySqlConnection(connectionString);
            connection.Open();
            Debug.Log("[SQLManager] DB 연결 성공");
        }
        catch (Exception e)
        {
            Debug.LogError($"[SQLManager] DB 연결 실패: {e.Message}");
        }
    }

    [Server]
    public RegisterResult Register(string loginId, string rawPassword, string nickname)
    {
        if (string.IsNullOrWhiteSpace(loginId) ||
            string.IsNullOrWhiteSpace(rawPassword) ||
            string.IsNullOrWhiteSpace(nickname))
        {
            return RegisterResult.InvalidInput;
        }

        try
        {

            using var connection = new MySqlConnection(connectionString);
            connection.Open();

            if (IsLoginIdExists(connection, loginId))
                return RegisterResult.DuplicateLoginId;

            if (IsNicknameExists(connection, nickname))
                return RegisterResult.DuplicateNickname;

            string passwordHash = HashPassword(rawPassword);

            const string query = @"
                INSERT INTO users (login_id, password_hash, nickname)
                VALUES (@loginId, @passwordHash, @nickname);
            ";

            using var cmd = new MySqlCommand(query, connection);
            cmd.Parameters.AddWithValue("@loginId", loginId);
            cmd.Parameters.AddWithValue("@passwordHash", passwordHash);
            cmd.Parameters.AddWithValue("@nickname", nickname);

            int result = cmd.ExecuteNonQuery();

            return result > 0 ? RegisterResult.Success : RegisterResult.Failed;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SQLManager] 회원가입 실패: {e.Message}");
            return RegisterResult.Failed;
        }
    }

    [Server]
    public LoginResult Login(string loginId, string rawPassword, out string nickname)
    {
        nickname = string.Empty;

        if (string.IsNullOrWhiteSpace(loginId) || string.IsNullOrWhiteSpace(rawPassword))
            return LoginResult.InvalidInput;

        try
        {
            using var connection = new MySqlConnection(connectionString);
            connection.Open();

            const string query = @"
                SELECT password_hash, nickname
                FROM users
                WHERE login_id = @loginId
                LIMIT 1;
            ";

            using var cmd = new MySqlCommand(query, connection);
            cmd.Parameters.AddWithValue("@loginId", loginId);

            using var reader = cmd.ExecuteReader();

            if (!reader.Read())
                return LoginResult.UserNotFound;

            string savedHash = reader.GetString("password_hash");
            nickname = reader.GetString("nickname");

            bool isValid = VerifyPassword(rawPassword, savedHash);
            return isValid ? LoginResult.Success : LoginResult.WrongPassword;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SQLManager] 로그인 실패: {e.Message}");
            return LoginResult.Failed;
        }
    }

    private bool IsLoginIdExists(MySqlConnection connection, string loginId)
    {
        const string query = @"
            SELECT COUNT(*)
            FROM users
            WHERE login_id = @loginId;
        ";

        using var cmd = new MySqlCommand(query, connection);
        cmd.Parameters.AddWithValue("@loginId", loginId);

        long count = (long)cmd.ExecuteScalar();
        return count > 0;
    }

    private bool IsNicknameExists(MySqlConnection connection, string nickname)
    {
        const string query = @"
            SELECT COUNT(*)
            FROM users
            WHERE nickname = @nickname;
        ";

        using var cmd = new MySqlCommand(query, connection);
        cmd.Parameters.AddWithValue("@nickname", nickname);

        long count = (long)cmd.ExecuteScalar();
        return count > 0;
    }

    private string HashPassword(string password)
    {
        byte[] salt = new byte[16];

        using var pbkdf2 = new Rfc2898DeriveBytes(
            password,
            salt,
            100_000,
            HashAlgorithmName.SHA256);

        byte[] hash = pbkdf2.GetBytes(32);

        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    private bool VerifyPassword(string password, string storedValue)
    {
        string[] parts = storedValue.Split(':');
        if (parts.Length != 2)
            return false;

        byte[] salt = Convert.FromBase64String(parts[0]);
        byte[] savedHash = Convert.FromBase64String(parts[1]);

        using var pbkdf2 = new Rfc2898DeriveBytes(
            password,
            salt,
            100_000,
            HashAlgorithmName.SHA256);

        byte[] computedHash = pbkdf2.GetBytes(32);

        return CryptographicOperations.FixedTimeEquals(savedHash, computedHash);
    }
}