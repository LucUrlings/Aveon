export type CurrentUser = {
  isAuthenticated: boolean
  id: string | null
  email: string | null
  roles: string[]
  defaultReturnRanking: 'best' | 'cheapest' | 'fastest' | null
}

export type AuthCredentials = {
  email: string
  password: string
}
