export type CurrentUser = {
  isAuthenticated: boolean
  id: string | null
  email: string | null
  roles: string[]
}

export type AuthCredentials = {
  email: string
  password: string
}
