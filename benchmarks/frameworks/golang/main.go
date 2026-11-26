// The benchmark app on Go (benchmarks/frameworks/README.md): the same routes, data and React pages as the other apps, written with
// Gin, GORM and gonertia. BENCH_SSR=1 renders first visits with the Node SSR server started by start.sh; otherwise the client renders them.
package main

import (
	"context"
	"crypto/rand"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"log"
	"net/http"
	"os"
	"sort"
	"strconv"
	"strings"
	"sync"
	"time"
	"unicode/utf8"

	"github.com/gin-gonic/gin"
	"github.com/redis/go-redis/v9"
	inertia "github.com/romsar/gonertia/v3"
	"gorm.io/driver/postgres"
	"gorm.io/gorm"
	"gorm.io/gorm/logger"
)

const (
	sessionCookie = "bench_session"
	dashboardKey  = "bench:dashboard"

	batches = 16
)

var (
	features = []Feature{
		{Title: "Lists", Body: "Group todos by project, tag and due date."},
		{Title: "Reports", Body: "See what got done this week and what slipped."},
		{Title: "Teams", Body: "Share lists and assign todos to people."},
	}
	assignees = []string{"ada", "barbara", "dennis", "grace", "linus"}

	messageTitle    = "Enter a title between 3 and 120 characters."
	messagePriority = "Choose a priority from 0 to 4."
	messageAssignee = "Choose someone from the team."
	messageDue      = "Enter a due date in 2026."
)

type Feature struct {
	Title string `json:"title"`
	Body  string `json:"body"`
}

type Todo struct {
	ID       int    `json:"id"`
	Title    string `json:"title"`
	Done     bool   `json:"done"`
	Priority int    `json:"priority"`
	Assignee string `json:"assignee"`
	Due      string `json:"due" gorm:"column:due;->"`
}

func (Todo) TableName() string { return "todos" }

type AssigneeStats struct {
	Assignee    string `json:"assignee"`
	Total       int    `json:"total"`
	Done        int    `json:"done"`
	PrioritySum int    `json:"prioritySum" gorm:"column:priority_sum"`
}

type Stats struct {
	Total      int             `json:"total"`
	Done       int             `json:"done"`
	ByAssignee []AssigneeStats `json:"byAssignee"`
}

// Submission is where POST /todos writes, separate from the todos the read routes page through.
type Submission struct {
	ID        int       `gorm:"primaryKey"`
	Title     string    `gorm:"column:title"`
	Priority  int       `gorm:"column:priority"`
	Assignee  string    `gorm:"column:assignee"`
	DueAt     time.Time `gorm:"column:due_at;type:date"`
	CreatedAt time.Time `gorm:"column:created_at"`
}

func (Submission) TableName() string { return "submissions" }

type app struct {
	db    *gorm.DB
	redis *redis.Client
	flash *flashStore
}

func main() {
	gin.SetMode(gin.ReleaseMode)

	a := &app{db: openDB(), redis: openRedis(), flash: newFlashStore()}
	options := []inertia.Option{inertia.WithFlashProvider(a.flash)}
	if os.Getenv("BENCH_SSR") == "1" {
		options = append(options, inertia.WithSSR())
	}
	i, err := inertia.NewFromFile("app.html", options...)
	if err != nil {
		log.Fatal(err)
	}
	if _, err := inertia.NewVite(i); err != nil {
		log.Fatal(err)
	}

	r := gin.New()
	r.Use(gin.Recovery(), a.session())
	r.Static("/build", "public/build")

	r.GET("/", route(i, a.home(i)))
	r.GET("/todos", route(i, a.todos(i)))
	r.GET("/dashboard", route(i, a.dashboard(i)))
	r.GET("/todos/new", route(i, a.newTodo(i)))
	r.POST("/todos", route(i, a.store(i)))
	r.GET("/compute", route(i, a.compute(i, false)))
	r.GET("/compute/parallel", route(i, a.compute(i, true)))

	port := os.Getenv("PORT")
	if port == "" {
		port = "8080"
	}
	log.Fatal(r.Run(":" + port))
}

// route runs a handler behind gonertia's middleware. The session middleware runs first, so the flash provider can find the session.
func route(i *inertia.Inertia, handler http.HandlerFunc) gin.HandlerFunc {
	h := i.Middleware(handler)
	return func(c *gin.Context) { h.ServeHTTP(c.Writer, c.Request) }
}

func render(w http.ResponseWriter, r *http.Request, i *inertia.Inertia, component string, props inertia.Props) {
	if err := i.Render(w, r, component, props); err != nil {
		log.Printf("render %s: %v", component, err)
		http.Error(w, "render error", http.StatusInternalServerError)
	}
}

func (a *app) home(i *inertia.Inertia) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		render(w, r, i, "Home", inertia.Props{"features": features})
	}
}

func (a *app) todos(i *inertia.Inertia) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		perPage := intParam(r, "perPage", 25, 1, 100)
		page := intParam(r, "page", 1, 1, 400)
		var todos []Todo
		err := a.db.WithContext(r.Context()).Model(&Todo{}).
			Select("id, title, done, priority, assignee, to_char(due_at, 'YYYY-MM-DD') AS due").
			Order("created_at DESC, id DESC").Limit(perPage).Offset((page - 1) * perPage).
			Find(&todos).Error
		if err != nil {
			log.Printf("todos: %v", err)
			http.Error(w, "database error", http.StatusInternalServerError)
			return
		}
		render(w, r, i, "Todos", inertia.Props{"todos": todos, "page": page, "perPage": perPage})
	}
}

func (a *app) dashboard(i *inertia.Inertia) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		stats, err := a.stats(r.Context())
		if err != nil {
			log.Printf("dashboard: %v", err)
			http.Error(w, "database error", http.StatusInternalServerError)
			return
		}
		render(w, r, i, "Dashboard", inertia.Props{"stats": stats})
	}
}

func (a *app) stats(ctx context.Context) (Stats, error) {
	if a.redis != nil {
		if cached, err := a.redis.Get(ctx, dashboardKey).Bytes(); err == nil {
			var stats Stats
			if json.Unmarshal(cached, &stats) == nil {
				return stats, nil
			}
		}
	}

	var rows []AssigneeStats
	err := a.db.WithContext(ctx).Model(&Todo{}).
		Select("assignee, count(*)::int AS total, (count(*) FILTER (WHERE done))::int AS done, sum(priority)::int AS priority_sum").
		Group("assignee").Order("assignee").Scan(&rows).Error
	if err != nil {
		return Stats{}, err
	}
	stats := Stats{ByAssignee: rows}
	for _, row := range rows {
		stats.Total += row.Total
		stats.Done += row.Done
	}
	if a.redis != nil {
		if encoded, err := json.Marshal(stats); err == nil {
			_ = a.redis.Set(ctx, dashboardKey, encoded, 60*time.Second).Err()
		}
	}
	return stats, nil
}

func (a *app) newTodo(i *inertia.Inertia) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		render(w, r, i, "Todos/New", nil)
	}
}

func (a *app) store(i *inertia.Inertia) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		var input newTodoInput
		if err := json.NewDecoder(r.Body).Decode(&input); err != nil {
			input = newTodoInput{}
		}

		errors, due, title := input.validate()
		if len(errors) > 0 {
			r = r.WithContext(inertia.SetValidationErrors(r.Context(), errors))
			i.Redirect(w, r, "/todos/new", http.StatusSeeOther)
			return
		}

		submission := Submission{Title: title, Priority: *input.Priority, Assignee: *input.Assignee, DueAt: due}
		if err := a.db.WithContext(r.Context()).Create(&submission).Error; err != nil {
			log.Printf("insert submission: %v", err)
			http.Error(w, "database error", http.StatusInternalServerError)
			return
		}
		i.Redirect(w, r, "/todos/new", http.StatusSeeOther)
	}
}

func (a *app) compute(i *inertia.Inertia, parallel bool) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		n := intParam(r, "n", 20000, 16, 1000000)
		render(w, r, i, "Compute", inertia.Props{"n": n, "checksum": computeChecksum(n, parallel), "parallel": parallel})
	}
}

type newTodoInput struct {
	Title    *string `json:"title"`
	Priority *int    `json:"priority"`
	Assignee *string `json:"assignee"`
	Due      *string `json:"due"`
}

// validate applies the rules of shared/ts/rules.ts and returns one message per failing field.
func (in newTodoInput) validate() (inertia.ValidationErrors, time.Time, string) {
	errors := inertia.ValidationErrors{}
	title := ""
	if in.Title != nil {
		title = strings.TrimSpace(*in.Title)
	}
	if n := utf8.RuneCountInString(title); n < 3 || n > 120 {
		errors["title"] = messageTitle
	}
	if in.Priority == nil || *in.Priority < 0 || *in.Priority > 4 {
		errors["priority"] = messagePriority
	}
	if in.Assignee == nil || !contains(assignees, *in.Assignee) {
		errors["assignee"] = messageAssignee
	}

	var due time.Time
	validDue := false
	if in.Due != nil && len(*in.Due) == 10 {
		parsed, err := time.Parse("2006-01-02", *in.Due)
		if err == nil && parsed.Year() == 2026 && parsed.Format("2006-01-02") == *in.Due {
			due, validDue = parsed, true
		}
	}
	if !validDue {
		errors["due"] = messageDue
	}
	return errors, due, title
}

func contains(values []string, value string) bool {
	for _, candidate := range values {
		if candidate == value {
			return true
		}
	}
	return false
}

func intParam(r *http.Request, name string, fallback, min, max int) int {
	value := r.URL.Query().Get(name)
	if value == "" {
		return fallback
	}
	number, err := strconv.Atoi(value)
	if err != nil {
		return fallback
	}
	return maxInt(min, minInt(max, number))
}

func minInt(a, b int) int {
	if a < b {
		return a
	}
	return b
}

func maxInt(a, b int) int {
	if a > b {
		return a
	}
	return b
}

type record struct {
	value int
	id    int
	group int
}

// computeChecksum is the algorithm of shared/ts/compute.ts and Compute.cs: 16 batches of n/16 records from a xorshift32 generator,
// each sorted by (value, id), grouped into 64 buckets and hashed with SHA-256; the checksum is the SHA-256 of the 16 batch digests.
func computeChecksum(n int, parallel bool) string {
	m := n / batches
	digests := make([]string, batches)
	if parallel {
		var wg sync.WaitGroup
		wg.Add(batches)
		for b := 0; b < batches; b++ {
			go func(b int) {
				defer wg.Done()
				digests[b] = computeBatch(b, m)
			}(b)
		}
		wg.Wait()
	} else {
		for b := 0; b < batches; b++ {
			digests[b] = computeBatch(b, m)
		}
	}
	return sha256Hex(strings.Join(digests, ""))
}

func computeBatch(b, m int) string {
	s := uint32(b+1) * 2654435761
	next := func() uint32 {
		s ^= s << 13
		s ^= s >> 17
		s ^= s << 5
		return s
	}

	records := make([]record, m)
	for i := 0; i < m; i++ {
		value := int(next() % 1000000)
		records[i] = record{value: value, id: b*m + i, group: int(next() % 64)}
	}
	sort.Slice(records, func(i, j int) bool {
		if records[i].value != records[j].value {
			return records[i].value < records[j].value
		}
		return records[i].id < records[j].id
	})

	var counts [64]int
	var sums [64]int64
	var text strings.Builder
	text.Grow(m*7 + 64*10)
	for index, r := range records {
		counts[r.group]++
		sums[r.group] += int64(r.value)
		if index > 0 {
			text.WriteByte(',')
		}
		text.WriteString(strconv.Itoa(r.id))
	}
	text.WriteByte('|')
	for group := 0; group < 64; group++ {
		if group > 0 {
			text.WriteByte(';')
		}
		text.WriteString(strconv.Itoa(group))
		text.WriteByte(':')
		text.WriteString(strconv.Itoa(counts[group]))
		text.WriteByte(':')
		text.WriteString(strconv.FormatInt(sums[group], 10))
	}
	return sha256Hex(text.String())
}

func sha256Hex(value string) string {
	sum := sha256.Sum256([]byte(value))
	return hex.EncodeToString(sum[:])
}

func openDB() *gorm.DB {
	dsn := os.Getenv("DATABASE_URL")
	if dsn == "" {
		dsn = "host=localhost user=bench password=bench dbname=bench port=5432 sslmode=disable"
	}
	db, err := gorm.Open(postgres.Open(dsn), &gorm.Config{
		Logger:                 logger.Default.LogMode(logger.Silent),
		SkipDefaultTransaction: true,
	})
	if err != nil {
		log.Fatal(err)
	}
	sqlDB, err := db.DB()
	if err != nil {
		log.Fatal(err)
	}
	sqlDB.SetMaxOpenConns(10)
	sqlDB.SetMaxIdleConns(10)
	return db
}

func openRedis() *redis.Client {
	url := os.Getenv("REDIS_URL")
	if url == "" {
		return nil
	}
	options, err := redis.ParseURL(url)
	if err != nil {
		log.Fatal(err)
	}
	return redis.NewClient(options)
}

// flashStore keeps the form's validation errors in memory, keyed by the session cookie the browser sends back after the redirect.
// Gonertia calls it twice per request: once to add stored errors to the page and once, on a redirect, to store them.
type flashStore struct {
	mu     sync.Mutex
	errors map[string]inertia.ValidationErrors
}

type sessionKey struct{}

func newFlashStore() *flashStore {
	return &flashStore{errors: make(map[string]inertia.ValidationErrors)}
}

func (s *flashStore) FlashErrors(ctx context.Context, errors inertia.ValidationErrors) error {
	session := sessionFrom(ctx)
	if session == "" {
		return nil
	}
	copied := make(inertia.ValidationErrors, len(errors))
	for key, value := range errors {
		copied[key] = value
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	s.errors[session] = copied
	return nil
}

func (s *flashStore) GetErrors(ctx context.Context) (inertia.ValidationErrors, error) {
	session := sessionFrom(ctx)
	s.mu.Lock()
	defer s.mu.Unlock()
	errors := s.errors[session]
	delete(s.errors, session)
	if errors == nil {
		errors = inertia.ValidationErrors{}
	}
	return errors, nil
}

func (s *flashStore) Flash(context.Context, inertia.Flash) error { return nil }

func (s *flashStore) GetFlash(context.Context) (inertia.Flash, error) {
	return inertia.Flash{}, nil
}

func (s *flashStore) FlashClearHistory(context.Context) error { return nil }

func (s *flashStore) ShouldClearHistory(context.Context) (bool, error) { return false, nil }

func (a *app) session() gin.HandlerFunc {
	return func(c *gin.Context) {
		session, err := c.Cookie(sessionCookie)
		if err != nil || session == "" {
			session = randomID()
			c.SetCookie(sessionCookie, session, 86400, "/", "", false, true)
		}
		c.Request = c.Request.WithContext(context.WithValue(c.Request.Context(), sessionKey{}, session))
		c.Next()
	}
}

func sessionFrom(ctx context.Context) string {
	session, _ := ctx.Value(sessionKey{}).(string)
	return session
}

func randomID() string {
	var bytes [16]byte
	if _, err := rand.Read(bytes[:]); err != nil {
		return strconv.FormatInt(time.Now().UnixNano(), 36)
	}
	return hex.EncodeToString(bytes[:])
}
