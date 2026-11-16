<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

// Where the new-todo form writes; created_at is filled by the column default.
class Submission extends Model
{
    public $timestamps = false;

    protected $fillable = ['title', 'priority', 'assignee', 'due_at'];
}
