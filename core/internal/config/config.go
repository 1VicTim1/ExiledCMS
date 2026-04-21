package config

import (
	"fmt"
	"os"
	"strconv"
	"strings"
)

type Config struct {
	ServiceName         string
	Environment         string
	HTTPPort            int
	MySQLDSN            string
	RedisAddr           string
	NATSURL             string
	ModuleConfigJSON    string
	SentryDSN           string
	SentryMinLevel      string
	LogBufferMaxEntries int
}

func Load() Config {
	return Config{
		ServiceName:         getEnv("SERVICE_NAME", "platform-core"),
		Environment:         getEnv("APP_ENV", "development"),
		HTTPPort:            getEnvInt("PORT", 8080),
		MySQLDSN:            getEnv("MYSQL_DSN", "exiledcms:exiledcms@tcp(localhost:3306)/exiledcms_platform?parseTime=true"),
		RedisAddr:           getEnv("REDIS_ADDR", "localhost:6379"),
		NATSURL:             getEnv("NATS_URL", "nats://localhost:4222"),
		ModuleConfigJSON:    getEnv("MODULE_CONFIG_JSON", ""),
		SentryDSN:           getEnv("SENTRY_DSN", ""),
		SentryMinLevel:      getEnv("SENTRY_MIN_LEVEL", "error"),
		LogBufferMaxEntries: getEnvInt("LOG_BUFFER_MAX_ENTRIES", 2000),
	}
}

func (c Config) HTTPAddr() string {
	return fmt.Sprintf(":%d", c.HTTPPort)
}

func (c Config) HTTPPortString() string {
	return strconv.Itoa(c.HTTPPort)
}

func (c Config) InfraStatus() map[string]string {
	return map[string]string{
		"mysql":  configured(c.MySQLDSN),
		"redis":  configured(c.RedisAddr),
		"nats":   configured(c.NATSURL),
		"sentry": configured(c.SentryDSN),
	}
}

func configured(value string) string {
	if strings.TrimSpace(value) == "" {
		return "missing"
	}

	return "configured"
}

func getEnv(key string, fallback string) string {
	if value := strings.TrimSpace(os.Getenv(key)); value != "" {
		return value
	}

	return fallback
}

func getEnvInt(key string, fallback int) int {
	raw := strings.TrimSpace(os.Getenv(key))
	if raw == "" {
		return fallback
	}

	parsed, err := strconv.Atoi(raw)
	if err != nil {
		return fallback
	}

	return parsed
}
